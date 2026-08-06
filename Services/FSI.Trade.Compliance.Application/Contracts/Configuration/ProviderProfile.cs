using System.Text.Json.Serialization;

namespace FSI.Trade.Compliance.Application.Contracts.Configuration;

// =====================================================================
// Provider Profile — the data-driven description of ANY third-party KYC
// provider. Editing it (from the UI builder or the config JSON editor) is
// all that is needed to onboard a new provider — no backend code changes.
// See KYC_PROVIDER_DYNAMIC_INTEGRATION_DESIGN.md.
// =====================================================================

public sealed class ProviderProfile
{
    public string? Name { get; set; }
    public string? ActiveEnvironment { get; set; }
    public Dictionary<string, ProviderEnvironment>? Environments { get; set; }
    public ProviderRequest? Request { get; set; }
    public ProviderAuth? Auth { get; set; }
    public ProviderSuccess? Success { get; set; }
    public Dictionary<string, FieldMap>? ResponseMapping { get; set; }
}

public sealed class ProviderEnvironment
{
    public string? BaseUrl { get; set; }
    public ProviderAuth? Auth { get; set; }
}

public sealed class ProviderRequest
{
    public string? Method { get; set; }          // GET | POST | PUT | PATCH | DELETE
    public string? Path { get; set; }             // appended to the environment base URL
    public Dictionary<string, string>? Headers { get; set; }
    public Dictionary<string, string>? Query { get; set; }
    public string? ContentType { get; set; }
    public string? BodyTemplate { get; set; }     // may contain {{placeholders}}
}

public sealed class ProviderAuth
{
    public string? Type { get; set; }             // None | Basic | Bearer | ApiKey | OAuth2 | Custom
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Token { get; set; }
    public ApiKeyAuth? ApiKey { get; set; }
    public OAuth2Auth? OAuth2 { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
}

public sealed class ApiKeyAuth
{
    public string? Name { get; set; }
    public string? In { get; set; }               // header | query
    public string? Value { get; set; }
}

public sealed class OAuth2Auth
{
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
}

public sealed class ProviderSuccess
{
    public string? StatusRule { get; set; }       // "200" | "2xx" | "any"
    public int[]?  StatusCodes { get; set; }      // explicit allow-list (overrides StatusRule)
    public List<JsonCondition>? JsonConditions { get; set; }  // ALL must hold
}

public sealed class JsonCondition
{
    public string? Path { get; set; }             // JSONPath into the response
    public string? Op { get; set; }               // eq | ne | contains | exists
    public string? Value { get; set; }
}

public sealed class FieldMap
{
    public string? Path { get; set; }             // JSONPath into the response
    public string? Type { get; set; }             // string | number | bool
    public string? Default { get; set; }
    public Dictionary<string, string>? ValueMap { get; set; }  // provider value -> app value
    public string? SplitOn { get; set; }          // optional pre-split (e.g. "|")
    public int?    SplitIndex { get; set; }       // segment to take after split
}

// ----- Test / preview result -----

public sealed class ProviderExecResult
{
    public ProviderRequestPreview? request { get; set; }
    public ProviderResponseInfo?   response { get; set; }
    public bool   reachable { get; set; }
    public bool   success { get; set; }
    public Dictionary<string, object?> mapped { get; set; } = new();
    public List<string> notes { get; set; } = new();
    public string? error { get; set; }
}

public sealed class ProviderRequestPreview
{
    public string method { get; set; } = "GET";
    public string url { get; set; } = "";
    public Dictionary<string, string> headers { get; set; } = new();   // secrets masked
    public string? body { get; set; }
}

public sealed class ProviderResponseInfo
{
    public int    status { get; set; }
    public string? statusText { get; set; }
    public bool   statusOk { get; set; }                                // 2xx
    public Dictionary<string, string> headers { get; set; } = new();
    public string? body { get; set; }
    public long   elapsedMs { get; set; }
}

/// <summary>
/// Executes a configured <see cref="ProviderProfile"/> — builds + sends the
/// request, evaluates success criteria, and maps the response. Used by both the
/// runtime KYC gate and the config-screen test harness.
/// </summary>
public interface IKycProviderEngine
{
    Task<ProviderExecResult> ExecuteAsync(IDictionary<string, string?> context, CancellationToken ct = default);
}
