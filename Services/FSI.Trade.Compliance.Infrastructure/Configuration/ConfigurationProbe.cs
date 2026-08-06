using System.Diagnostics;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>
/// Verification probes for the Configuration screen. Results are sanitised —
/// never a connection string, secret, auth header or unredacted PII.
/// </summary>
public sealed class ConfigurationProbe : IConfigurationProbe
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IKycScreeningService _screening;
    private readonly IKycProviderEngine _engine;
    private readonly ISecretConfigReader _secrets;

    public ConfigurationProbe(IConfiguration config, IHttpClientFactory httpFactory,
                              IKycScreeningService screening, IKycProviderEngine engine,
                              ISecretConfigReader secrets)
    { _config = config; _httpFactory = httpFactory; _screening = screening; _engine = engine; _secrets = secrets; }

    /// <summary>Rich provider test — the real request/response/mapping/timing envelope.</summary>
    public Task<ProviderExecResult> TestProviderAsync(IDictionary<string, string?> context, CancellationToken ct)
        => _engine.ExecuteAsync(context, ct);

    public async Task<ProbeResult> TestFccmConnectionAsync(CancellationToken ct)
    {
        // Source of truth is the ENCRYPTED value in the Configuration module
        // (decrypted here at use); fall back to appsettings only if unset.
        var conn = _secrets.Get("Integration.FccmOracleConnection");
        if (string.IsNullOrWhiteSpace(conn))
            conn = _config["Integration:FccmOracleConnection"];
        if (string.IsNullOrWhiteSpace(conn))
            return new ProbeResult(false, 0, "FCCM connection is not configured.", "not_configured");

        var sw = Stopwatch.StartNew();
        try
        {
            await using var con = new OracleConnection(conn);
            await con.OpenAsync(ct);
            await using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM DUAL";
            _ = await cmd.ExecuteScalarAsync(ct);
            sw.Stop();
            return new ProbeResult(true, sw.ElapsedMilliseconds, "SELECT 1 FROM DUAL succeeded", "ok");
        }
        catch (OracleException ox)
        {
            sw.Stop();
            // Return only the sanitised ORA class, never the message (may echo host/creds).
            return new ProbeResult(false, sw.ElapsedMilliseconds, $"ORA-{ox.Number:00000}", $"ORA-{ox.Number:00000}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ProbeResult(false, sw.ElapsedMilliseconds, "Timeout", "timeout");
        }
        catch
        {
            sw.Stop();
            return new ProbeResult(false, sw.ElapsedMilliseconds, "Connection failed", "error");
        }
    }

    public async Task<UrlProbeResult> TestUrlAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new UrlProbeResult(false, 0, null, null, false, "Invalid URL");

        var client = _httpFactory.CreateClient("config-probe");
        client.Timeout = TimeSpan.FromSeconds(6);
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var code = (int)resp.StatusCode;
            var statusOk = code is >= 200 and < 300;
            // Report the ACTUAL status honestly. "Reachable" (host answered) is a
            // SEPARATE signal from "statusOk" (a 2xx) — a 404/500 is reachable but
            // NOT ok. The message shows the real code + reason.
            var msg = statusOk
                ? $"HTTP {code} {resp.ReasonPhrase} — OK"
                : $"HTTP {code} {resp.ReasonPhrase} — reachable but NOT a success status";
            return new UrlProbeResult(true, sw.ElapsedMilliseconds, code, resp.ReasonPhrase, statusOk, msg);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new UrlProbeResult(false, sw.ElapsedMilliseconds, null, null, false, "Timeout");
        }
        catch (HttpRequestException hx)
        {
            sw.Stop();
            var cls = hx.Message.Contains("No such host", StringComparison.OrdinalIgnoreCase) ? "DNS failure"
                    : hx.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) ? "Connection refused"
                    : "Unreachable";
            return new UrlProbeResult(false, sw.ElapsedMilliseconds, null, null, false, cls);
        }
        catch
        {
            sw.Stop();
            return new UrlProbeResult(false, sw.ElapsedMilliseconds, null, null, false, "Unreachable");
        }
    }

    public async Task<OnboardingCallResult> TestOnboardingCallAsync(string customerNo, CancellationToken ct)
    {
        // Dry run through the SAME screening adapter the KYC flow uses. The adapter
        // already applies the provider mapping, so 'mapped' is its standardised
        // KycResult. TODO(Phase 2): also surface the true RAW provider body and let
        // the configurable Onboarding.ResponseMapping profile drive the mapping here.
        var baseUrl = _config["Onboarding:BaseUrl"] ?? _config["Integration:Brains:BaseUrl"] ?? "";
        var request = new
        {
            method = "GET",
            url = ConfigMask(baseUrl),
            query = new { CUSTNO = customerNo }
        };
        try
        {
            var result = await _screening.GetKycForCustomerAsync(customerNo, ct);
            if (result is null)
                return new OnboardingCallResult(request, null, null, null,
                    "Provider returned no record (unreachable, or customer not present).");

            var raw = new { riskScore = result.RiskScore, customerName = result.CustomerName };
            var adverse = result.RiskScore is "PEP" or "High";
            var mapped = new
            {
                riskScore = result.RiskScore,
                decision = adverse ? "adverse (blocked under 'Block' policy)" : "pass"
            };
            return new OnboardingCallResult(request, raw, mapped, null, null);
        }
        catch (Exception ex)
        {
            return new OnboardingCallResult(request, null, null, null, Sanitise(ex));
        }
    }

    public Task<FileValidationResult> ValidateFileAsync(string schemaKey, string content, CancellationToken ct)
    {
        // Phase 1: parse-validate the JSON (well-formedness + a couple of required
        // roots for the onboarding schema). TODO(Phase 2): full JSON-Schema
        // validation via NJsonSchema against the seeded onboarding.v2 schema.
        var errors = new List<FileValidationError>();
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(content); }
        catch (JsonException jx)
        {
            errors.Add(new FileValidationError($"line {jx.LineNumber}", jx.Message));
            return Task.FromResult(new FileValidationResult(false, errors));
        }

        if (schemaKey.Contains("Onboarding", StringComparison.OrdinalIgnoreCase))
        {
            var root = doc!.RootElement;
            foreach (var required in new[] { "requestUserId", "applicationId", "OnboardingCustomer" })
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(required, out _))
                    errors.Add(new FileValidationError($"$.{required}", "required property missing"));
        }
        return Task.FromResult(new FileValidationResult(errors.Count == 0, errors));
    }

    private static string Sanitise(Exception ex) => ex switch
    {
        OperationCanceledException => "Timeout",
        HttpRequestException => "Provider unreachable",
        _ => "Provider call failed"
    };

    private static string ConfigMask(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        return System.Text.RegularExpressions.Regex.Replace(url, @"(?i)(password|pwd|token|key)=([^&]*)", "$1=•••");
    }
}
