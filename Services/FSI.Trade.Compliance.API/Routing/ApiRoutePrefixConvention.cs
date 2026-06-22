using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace FSI.Trade.Compliance.API.Routing;

/// <summary>
/// Rewrites the leading <c>api</c> segment of every attribute route to a
/// configurable prefix (<c>Api:RoutePrefix</c>). Controllers keep their
/// conventional <c>[Route("api/v{version}/[controller]")]</c> templates, so
/// local development still serves <c>/api/v1/...</c> out of the box.
///
/// Why this exists: when the API is published under an IIS application / virtual
/// directory that is itself named <c>api</c>, IIS already injects that segment
/// into the public path. Without this, the effective URL becomes
/// <c>https://host/api/api/v1/...</c> (doubled prefix). Set
/// <c>Api:RoutePrefix</c> to <c>""</c> in that environment and the controller-side
/// <c>api/</c> is dropped, so the public URL stays a single <c>.../api/v1/...</c>.
///
/// Behaviour:
///   • key absent            → prefix "api"  → routes unchanged (api/v1/...)
///   • "Api:RoutePrefix"="" → prefix ""     → leading api/ stripped (v1/...)
///   • "Api:RoutePrefix"="gateway" → api/ replaced with gateway/ (gateway/v1/...)
///
/// Only routes whose template begins with the <c>api</c> segment are touched;
/// any other attribute route is left exactly as authored.
/// </summary>
public sealed class ApiRoutePrefixConvention : IApplicationModelConvention
{
    private const string ApiSegment = "api";

    // Normalised: no surrounding whitespace, no leading/trailing slashes. May be "".
    private readonly string _prefix;

    public ApiRoutePrefixConvention(string? prefix)
    {
        var p = (prefix ?? ApiSegment).Trim().Trim('/');

        // Windows cannot hold an EMPTY environment variable — assigning an empty
        // value deletes the variable — so `Api__RoutePrefix=` (empty) read back as
        // null and silently fell through to the "api" default. To allow disabling
        // the prefix from an env var / web.config (the usual IIS knobs), treat a
        // few explicit sentinels as "no prefix". JSON appsettings can still use "".
        if (p.Length == 0
            || p.Equals("none",   StringComparison.OrdinalIgnoreCase)
            || p.Equals("(none)", StringComparison.OrdinalIgnoreCase)
            || p.Equals("-",      StringComparison.OrdinalIgnoreCase))
        {
            p = "";
        }

        _prefix = p;
    }

    public void Apply(ApplicationModel application)
    {
        // Nothing to do when the prefix is already "api" — routes are authored that way.
        if (string.Equals(_prefix, ApiSegment, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                var template = selector.AttributeRouteModel?.Template;
                if (string.IsNullOrEmpty(template)) continue;

                var rewritten = Rewrite(template);
                if (!ReferenceEquals(rewritten, template))
                    selector.AttributeRouteModel!.Template = rewritten;
            }
        }
    }

    private string Rewrite(string template)
    {
        // Whole template is exactly "api".
        if (template.Equals(ApiSegment, StringComparison.OrdinalIgnoreCase))
            return _prefix;

        // Leading "api/" segment.
        if (template.StartsWith(ApiSegment + "/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = template.Substring(ApiSegment.Length + 1); // text after "api/"
            return _prefix.Length == 0 ? rest : $"{_prefix}/{rest}";
        }

        // Not an api-prefixed route — leave untouched (same reference signals "no change").
        return template;
    }
}
