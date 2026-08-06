using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Kyc;

/// <summary>
/// Provider-agnostic KYC screening engine. Reads the configured
/// <see cref="ProviderProfile"/> (Configuration &gt; Onboarding.ProviderProfile),
/// builds + sends the request (method, URL, headers, auth, query, body template
/// with {{placeholders}}), evaluates the profile's success criteria, and maps the
/// response via JSONPath + value maps + defaults + types. Onboarding a new
/// provider is a profile edit — NO code change. If no profile is configured, a
/// built-in default profile (the legacy BRAINS GET shape) is used.
/// </summary>
public sealed class DynamicKycScreeningService : IKycScreeningService, IKycProviderEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<DynamicKycScreeningService> _log;

    public DynamicKycScreeningService(HttpClient http, IConfiguration config, ILogger<DynamicKycScreeningService> log)
    { _http = http; _config = config; _log = log; }

    // ---- IKycScreeningService (runtime gate) ----
    public async Task<KycResult?> GetKycForCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var ctx = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerCode"] = customerId,
            // TODO(follow-up): enrich the runtime context with CustomerName/CNIC/DOB
            // from the create command so POST-template providers can use them.
            ["Now"] = DateTime.UtcNow.ToFileTimeUtc().ToString(),
            ["Guid"] = Guid.NewGuid().ToString(),
        };
        var r = await ExecuteAsync(ctx, ct);
        if (!r.success) return null;
        var band = r.mapped.TryGetValue("riskLevel", out var b) ? b?.ToString() : null;
        if (string.IsNullOrWhiteSpace(band) || string.Equals(band, "Unknown", StringComparison.OrdinalIgnoreCase))
            return null;
        var name = r.mapped.TryGetValue("customerName", out var n) ? n?.ToString() : null;
        return new KycResult { RiskScore = band, CustomerName = name };
    }

    // ---- IKycProviderEngine (runtime + test harness) ----
    public async Task<ProviderExecResult> ExecuteAsync(IDictionary<string, string?> context, CancellationToken ct = default)
    {
        var result = new ProviderExecResult();
        var profile = LoadProfile();
        var (baseUrl, auth) = ResolveEnvironment(profile);
        var req = profile.Request ?? new ProviderRequest { Method = "GET" };

        try
        {
            // ---- build URL + query ----
            var method = new HttpMethod((req.Method ?? "GET").Trim().ToUpperInvariant());
            var url = (baseUrl ?? "").TrimEnd('/') + (string.IsNullOrEmpty(req.Path) ? "" : "/" + req.Path!.TrimStart('/'));
            var query = new List<string>();
            foreach (var kv in req.Query ?? new())
                query.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Resolve(kv.Value, context)));

            var headers = new Dictionary<string, string>();
            foreach (var kv in req.Headers ?? new())
                headers[kv.Key] = Resolve(kv.Value, context);

            // ---- auth ----
            await ApplyAuthAsync(auth, context, headers, query, ct);

            var fullUrl = url + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            string? body = null;
            if (!string.IsNullOrWhiteSpace(req.BodyTemplate))
                body = Resolve(req.BodyTemplate, context);

            using var msg = new HttpRequestMessage(method, fullUrl);
            foreach (var kv in headers)
                msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            if (body is not null)
                msg.Content = new StringContent(body, Encoding.UTF8, req.ContentType ?? "application/json");

            result.request = new ProviderRequestPreview
            {
                method = method.Method,
                url = Mask(fullUrl),
                headers = headers.ToDictionary(k => k.Key, v => MaskHeader(v.Key, v.Value)),
                body = body,
            };

            // ---- send ----
            var sw = Stopwatch.StartNew();
            using var resp = await _http.SendAsync(msg, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();
            result.reachable = true;

            var respHeaders = new Dictionary<string, string>();
            foreach (var h in resp.Headers) respHeaders[h.Key] = string.Join(", ", h.Value);
            foreach (var h in resp.Content.Headers) respHeaders[h.Key] = string.Join(", ", h.Value);

            var status = (int)resp.StatusCode;
            result.response = new ProviderResponseInfo
            {
                status = status,
                statusText = resp.ReasonPhrase,
                statusOk = status is >= 200 and < 300,
                headers = respHeaders,
                body = respBody.Length > 4000 ? respBody[..4000] + "…" : respBody,
                elapsedMs = sw.ElapsedMilliseconds,
            };

            // ---- success criteria ----
            JsonElement? root = null;
            try { root = JsonDocument.Parse(respBody).RootElement.Clone(); } catch { /* non-JSON body */ }
            result.success = EvaluateSuccess(profile.Success, status, root, result.notes);

            // ---- response mapping ----
            if (result.success && root is JsonElement el)
                foreach (var kv in profile.ResponseMapping ?? new())
                    result.mapped[kv.Key] = MapField(el, kv.Value);
        }
        catch (Exception ex)
        {
            result.error = Sanitise(ex);
            result.notes.Add(result.error);
        }
        return result;
    }

    // ================= profile loading =================
    private ProviderProfile LoadProfile()
    {
        // The DB→IConfiguration bridge flattens the Json config value into child
        // keys, so we bind the section straight into the POCO.
        var bound = _config.GetSection("Onboarding:ProviderProfile").Get<ProviderProfile>();
        if (bound?.Request is not null || bound?.Environments is not null || bound?.ResponseMapping is not null)
            return bound!;
        return DefaultProfile();
    }

    /// <summary>Built-in fallback = the legacy BRAINS GET shape (STATUS/TRANSOK).</summary>
    private ProviderProfile DefaultProfile()
    {
        var baseUrl = _config["Onboarding:BaseUrl"] ?? _config["Integration:Brains:BaseUrl"] ?? "";
        return new ProviderProfile
        {
            Name = "Default (BRAINS)",
            ActiveEnvironment = "Default",
            Environments = new() { ["Default"] = new ProviderEnvironment { BaseUrl = baseUrl } },
            Request = new ProviderRequest
            {
                Method = "GET",
                Query = new()
                {
                    ["APPLY_NO"] = "001", ["TASK_NO"] = "{{Now}}",
                    ["ZONENO"] = _config["Integration:Brains:ZoneNo"] ?? "6001",
                    ["ADJUST_TYPE"] = _config["Integration:Brains:AdjustType"] ?? "1",
                    ["CUSTNO"] = "{{CustomerCode}}", ["REG_TYPE"] = "", ["REG_NO"] = "",
                },
            },
            Auth = new ProviderAuth { Type = "None" },
            Success = new ProviderSuccess { StatusRule = "2xx", JsonConditions = new() { new JsonCondition { Path = "$.TRANSOK", Op = "eq", Value = "0" } } },
            ResponseMapping = new()
            {
                ["riskLevel"] = new FieldMap { Path = "$.STATUS", SplitOn = "|", SplitIndex = 0, ValueMap = new() { ["0"] = "PEP", ["1"] = "High", ["2"] = "Medium", ["3"] = "Low" }, Default = "Unknown" },
                ["customerName"] = new FieldMap { Path = "$.STATUS", SplitOn = "|", SplitIndex = 1 },
            },
        };
    }

    private (string? baseUrl, ProviderAuth? auth) ResolveEnvironment(ProviderProfile p)
    {
        ProviderEnvironment? env = null;
        if (p.Environments is { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(p.ActiveEnvironment) && p.Environments.TryGetValue(p.ActiveEnvironment!, out var e))
                env = e;
            else env = p.Environments.Values.First();
        }
        // SINGLE SOURCE OF TRUTH for the URL: the active environment's BaseUrl, which the
        // Provider Integration Builder shows and edits (Environments tab). The runtime gate
        // and the Test button both call exactly this value — never a hidden one. The legacy
        // flat Onboarding.BaseUrl (now retired) and the Brains URL remain fallbacks only for
        // an empty profile URL, so nothing breaks during transition.
        var baseUrl = !string.IsNullOrWhiteSpace(env?.BaseUrl)
            ? env!.BaseUrl
            : (_config["Onboarding:BaseUrl"] ?? _config["Integration:Brains:BaseUrl"]);
        var auth = env?.Auth ?? p.Auth ?? new ProviderAuth { Type = "None" };
        return (baseUrl, auth);
    }

    // ================= auth =================
    private async Task ApplyAuthAsync(ProviderAuth? auth, IDictionary<string, string?> ctx,
                                      Dictionary<string, string> headers, List<string> query, CancellationToken ct)
    {
        if (auth is null) return;
        switch ((auth.Type ?? "None").Trim().ToLowerInvariant())
        {
            case "basic":
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    Resolve(auth.Username, ctx) + ":" + Resolve(auth.Password, ctx)));
                headers["Authorization"] = "Basic " + creds;
                break;
            case "bearer":
                headers["Authorization"] = "Bearer " + Resolve(auth.Token, ctx);
                break;
            case "apikey":
                var name = auth.ApiKey?.Name ?? "X-API-Key";
                var val = Resolve(auth.ApiKey?.Value, ctx);
                if (string.Equals(auth.ApiKey?.In, "query", StringComparison.OrdinalIgnoreCase))
                    query.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(val));
                else headers[name] = val;
                break;
            case "custom":
                foreach (var kv in auth.CustomHeaders ?? new()) headers[kv.Key] = Resolve(kv.Value, ctx);
                break;
            case "oauth2":
                var token = await FetchOAuthTokenAsync(auth.OAuth2, ctx, ct);
                if (token is not null) headers["Authorization"] = "Bearer " + token;
                break;
        }
    }

    private async Task<string?> FetchOAuthTokenAsync(OAuth2Auth? o, IDictionary<string, string?> ctx, CancellationToken ct)
    {
        // Client-credentials grant. TODO(follow-up): cache the token until expiry.
        if (o is null || string.IsNullOrWhiteSpace(o.TokenUrl)) return null;
        try
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = Resolve(o.ClientId, ctx),
                ["client_secret"] = Resolve(o.ClientSecret, ctx),
            };
            if (!string.IsNullOrWhiteSpace(o.Scope)) form["scope"] = o.Scope!;
            using var resp = await _http.PostAsync(o.TokenUrl, new FormUrlEncodedContent(form), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning("OAuth2 token fetch failed: {Msg}", ex.Message);
            return null;
        }
    }

    // ================= success =================
    private static bool EvaluateSuccess(ProviderSuccess? s, int status, JsonElement? root, List<string> notes)
    {
        s ??= new ProviderSuccess { StatusRule = "2xx" };
        var statusOk =
            s.StatusCodes is { Length: > 0 } ? s.StatusCodes.Contains(status)
            : (s.StatusRule ?? "2xx").Trim().ToLowerInvariant() switch
            {
                "any" => true,
                "2xx" => status is >= 200 and < 300,
                var r when int.TryParse(r, out var code) => status == code,
                _ => status is >= 200 and < 300,
            };
        if (!statusOk) { notes.Add($"status {status} not allowed by success rule"); return false; }

        foreach (var c in s.JsonConditions ?? new())
        {
            var actual = root is JsonElement el ? JsonPathResolver.ResolveString(el, c.Path) : null;
            var ok = (c.Op ?? "eq").Trim().ToLowerInvariant() switch
            {
                "eq" => string.Equals(actual, c.Value, StringComparison.OrdinalIgnoreCase),
                "ne" => !string.Equals(actual, c.Value, StringComparison.OrdinalIgnoreCase),
                "contains" => actual is not null && c.Value is not null && actual.Contains(c.Value, StringComparison.OrdinalIgnoreCase),
                "exists" => actual is not null,
                _ => true,
            };
            if (!ok) { notes.Add($"condition failed: {c.Path} {c.Op} {c.Value} (actual='{actual}')"); return false; }
        }
        return true;
    }

    // ================= mapping =================
    private static object? MapField(JsonElement root, FieldMap m)
    {
        var raw = JsonPathResolver.ResolveString(root, m.Path);
        if (raw is not null && !string.IsNullOrEmpty(m.SplitOn))
        {
            var parts = raw.Split(m.SplitOn);
            var idx = m.SplitIndex ?? 0;
            raw = idx >= 0 && idx < parts.Length ? parts[idx].Trim() : null;
        }
        if (raw is not null && m.ValueMap is { Count: > 0 } && m.ValueMap.TryGetValue(raw, out var mapped))
            raw = mapped;
        if (string.IsNullOrEmpty(raw)) raw = m.Default;
        if (raw is null) return null;

        return (m.Type ?? "string").Trim().ToLowerInvariant() switch
        {
            "number" => decimal.TryParse(raw, out var d) ? d : raw,
            "bool" => bool.TryParse(raw, out var b) ? b : (raw == "1" || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)),
            _ => raw,
        };
    }

    // ================= helpers =================
    private string Resolve(string? template, IDictionary<string, string?> ctx)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([^}]+?)\s*\}\}", mtch =>
        {
            var key = mtch.Groups[1].Value.Trim();
            if (key.StartsWith("secret:", StringComparison.OrdinalIgnoreCase))
                return _config[key.Substring("secret:".Length)] ?? "";
            if (string.Equals(key, "Now", StringComparison.OrdinalIgnoreCase)) return DateTime.UtcNow.ToFileTimeUtc().ToString();
            if (string.Equals(key, "Guid", StringComparison.OrdinalIgnoreCase)) return Guid.NewGuid().ToString();
            return ctx.TryGetValue(key, out var v) ? v ?? "" : "";
        });
    }

    private static string MaskHeader(string name, string value)
        => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
           || name.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0
           || name.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
            ? "••••••" : value;

    private static string Mask(string url)
        => System.Text.RegularExpressions.Regex.Replace(url, @"(?i)(password|pwd|token|key|secret)=([^&]*)", "$1=•••");

    private static string Sanitise(Exception ex) => ex switch
    {
        TaskCanceledException => "Timeout",
        HttpRequestException => "Provider unreachable",
        _ => "Provider call failed: " + ex.GetType().Name,
    };
}
