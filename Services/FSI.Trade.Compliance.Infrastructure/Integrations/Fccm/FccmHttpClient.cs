using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// Thin HTTP wrapper around FCCM's case-onboarding endpoint
/// (<c>FccmOnboardingUrl</c>). Submits a case payload, returns FCCM's
/// request handle. Does NOT block waiting for the case ID — that's the
/// poller's job.
/// </summary>
public class FccmHttpClient
{
    private readonly HttpClient                _http;
    private readonly IntegrationOptions        _opt;
    private readonly ILogger<FccmHttpClient>   _log;
    private readonly ISecretConfigReader       _secrets;

    public FccmHttpClient(HttpClient http, IOptions<IntegrationOptions> opt,
                          ILogger<FccmHttpClient> log, ISecretConfigReader secrets)
    {
        _http = http;
        _opt  = opt.Value;
        _log  = log;
        _secrets = secrets;
    }

    // Wire format MUST keep PascalCase keys (OnboardingCustomer, ApplicantID,
    // …) and omit null fields — matches the legacy KYCRequestModel JSON.
    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Submits a KYC onboarding case to FCCM from the typed (hardcoded-path)
    /// payload. Returns the upstream request ID. See <see cref="PostJsonAsync"/>.
    /// </summary>
    public Task<FccmSubmitResult> SubmitCaseAsync(
        FccmOnboardingPayload payload, string customerId, CancellationToken ct = default)
        => PostJsonAsync(JsonSerializer.Serialize(payload, WireJson), customerId, ct);

    /// <summary>
    /// Submits a KYC onboarding case from a mapper-built <see cref="JsonNode"/>
    /// (config-driven path). The node already contains exactly what the active
    /// mapping profile chose to emit (null-omission handled during mapping).
    /// </summary>
    public Task<FccmSubmitResult> SubmitCaseAsync(
        JsonNode payload, string customerId, CancellationToken ct = default)
        => PostJsonAsync(payload.ToJsonString(WireJson), customerId, ct);

    /// <summary>
    /// Core POST: sends the pre-serialized JSON body to the FCCM onboarding
    /// endpoint with HTTP Basic auth (Integration:Onboarding Username/Password),
    /// and extracts the upstream request id. Null RequestId = submit failed;
    /// the caller logs the case as Failed.
    /// </summary>
    private async Task<FccmSubmitResult> PostJsonAsync(
        string json, string customerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.FccmOnboardingUrl))
        {
            _log.LogWarning("FccmOnboardingUrl not configured. SubmitCaseAsync returning null (no-op).");
            return new FccmSubmitResult(null, "FccmOnboardingUrl not configured.", json, null, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _opt.FccmOnboardingUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            // Basic auth (legacy: Convert.ToBase64String(user:pass)). Username comes
            // from the bridged Integration:Onboarding block; the PASSWORD is a secret,
            // read decrypted from the Configuration module (falls back to the
            // appsettings value only if not yet set there). Never logged.
            var authUser = _opt.Onboarding.Username;
            var authPass = _secrets.Get("Integration.Onboarding.Password") ?? _opt.Onboarding.Password;
            if (!string.IsNullOrWhiteSpace(authUser) && string.IsNullOrWhiteSpace(authPass))
                _log.LogError("FCCM onboarding password is not configured (Integration.Onboarding.Password in the Configuration module, nor an appsettings fallback). The Basic auth header will carry an empty password.");
            if (!string.IsNullOrWhiteSpace(authUser))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{authUser}:{authPass}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            // Verbose diagnostics (Integration:Diagnostics:FccmVerbose) — the exact
            // outgoing onboarding payload. Debug level: only emitted/flushed to the
            // FCCM diagnostics file when verbose is on; no-op otherwise.
            _log.LogDebug("FCCM onboarding REQUEST for {CustomerId} -> {Url}\n{Payload}",
                customerId, _opt.FccmOnboardingUrl, json);

            using var resp = await _http.SendAsync(request, ct);
            var bodyText = await resp.Content.ReadAsStringAsync(ct);

            // Milestone (always on): the exact URL called + HTTP status, so the
            // outbound FCCM call is always traceable. Auth is a Basic header (never
            // logged). Full request payload + response body are the Debug lines below.
            _log.LogInformation("FCCM onboarding POST {Url} -> HTTP {Status} for customer {CustomerId}.",
                _opt.FccmOnboardingUrl, (int)resp.StatusCode, customerId);
            _log.LogDebug("FCCM onboarding RESPONSE for {CustomerId}: HTTP {Status}\n{Body}",
                customerId, (int)resp.StatusCode, bodyText);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("FCCM submit returned HTTP {Status} for {CustomerId}: {Body}",
                    resp.StatusCode, customerId, bodyText);
                return new FccmSubmitResult(null, $"HTTP {(int)resp.StatusCode}: {Trunc(bodyText)}", json, bodyText, (int)resp.StatusCode);
            }

            // Tolerate either { "requestId": "..." } or { "RequestID": ... }.
            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "requestId", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(prop.Name, "RequestID", StringComparison.OrdinalIgnoreCase))
                    {
                        var rid = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(rid)) return new FccmSubmitResult(rid, null, json, bodyText, (int)resp.StatusCode);
                    }
                }
            }

            _log.LogWarning("FCCM submit response for {CustomerId} contained no requestId: {Body}", customerId, bodyText);
            return new FccmSubmitResult(null, $"2xx but no RequestID. Body: {Trunc(bodyText)}", json, bodyText, (int)resp.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "FCCM submit HTTP failure for {CustomerId}.", customerId);
            return new FccmSubmitResult(null, $"HTTP failure: {ex.Message}", json, null, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogError("FCCM submit HTTP timeout for {CustomerId} after {Timeout}.", customerId, _opt.HttpTimeout);
            return new FccmSubmitResult(null, $"Timeout after {_opt.HttpTimeout}.", json, null, null);
        }
    }

    private static string Trunc(string? s) =>
        string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= 480 ? s : s[..480]);
}

/// <summary>Result of an FCCM onboarding submit — carries the raw request body + response
/// (status + body) so the caller can persist them on the KycCaseRequest for diagnostics.</summary>
public sealed record FccmSubmitResult(
    string? RequestId, string? Error, string RequestBody, string? ResponseBody, int? StatusCode);

/// <summary>Append-only, timestamped, size-capped diagnostics trail for the FCCM exchange,
/// stored on <c>KycCaseRequest.Fccm_Response_Payload</c> and shared by the submit + the poller.</summary>
public static class FccmExchangeLog
{
    private const int Cap = 16000;   // keep the column bounded — retain the most recent tail
    public static string Append(string? existing, string line)
    {
        var stamped  = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {line}";
        var combined = string.IsNullOrEmpty(existing) ? stamped : existing + "\n" + stamped;
        return combined.Length <= Cap ? combined : combined[^Cap..];
    }
}
