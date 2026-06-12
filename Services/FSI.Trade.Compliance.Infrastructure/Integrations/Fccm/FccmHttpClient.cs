using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FSI.Trade.Compliance.Application.Common.Options;
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

    public FccmHttpClient(HttpClient http, IOptions<IntegrationOptions> opt, ILogger<FccmHttpClient> log)
    {
        _http = http;
        _opt  = opt.Value;
        _log  = log;
    }

    // Wire format MUST keep PascalCase keys (OnboardingCustomer, ApplicantID,
    // …) and omit null fields — matches the legacy KYCRequestModel JSON.
    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Submits a KYC onboarding case to FCCM. Returns the upstream request ID
    /// (their handle for the in-flight provisioning). Null = submit failed;
    /// caller logs the case as Failed. Sends HTTP Basic auth
    /// (FccmOnboardingUsername/Password) — legacy parity.
    /// </summary>
    public async Task<(string? RequestId, string? Error)> SubmitCaseAsync(
        FccmOnboardingPayload payload,
        string customerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.FccmOnboardingUrl))
        {
            _log.LogWarning("FccmOnboardingUrl not configured. SubmitCaseAsync returning null (no-op).");
            return (null, "FccmOnboardingUrl not configured.");
        }

        try
        {
            var json = JsonSerializer.Serialize(payload, WireJson);
            using var request = new HttpRequestMessage(HttpMethod.Post, _opt.FccmOnboardingUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            // Basic auth (legacy: Convert.ToBase64String(user:pass)). Prefer the
            // configurable Integration:Onboarding block; fall back to the
            // deprecated top-level FccmOnboardingUsername/Password.
            var authUser = !string.IsNullOrWhiteSpace(_opt.Onboarding.Username)
                ? _opt.Onboarding.Username : _opt.FccmOnboardingUsername;
            var authPass = !string.IsNullOrWhiteSpace(_opt.Onboarding.Username)
                ? _opt.Onboarding.Password : _opt.FccmOnboardingPassword;
            if (!string.IsNullOrWhiteSpace(authUser))
            {
                var token = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{authUser}:{authPass}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            using var resp = await _http.SendAsync(request, ct);
            var bodyText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("FCCM submit returned HTTP {Status} for {CustomerId}: {Body}",
                    resp.StatusCode, customerId, bodyText);
                return (null, $"HTTP {(int)resp.StatusCode}: {Trunc(bodyText)}");
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
                        if (!string.IsNullOrWhiteSpace(rid)) return (rid, null);
                    }
                }
            }

            _log.LogWarning("FCCM submit response for {CustomerId} contained no requestId: {Body}", customerId, bodyText);
            return (null, $"2xx but no RequestID. Body: {Trunc(bodyText)}");
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "FCCM submit HTTP failure for {CustomerId}.", customerId);
            return (null, $"HTTP failure: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogError("FCCM submit HTTP timeout for {CustomerId} after {Timeout}.", customerId, _opt.HttpTimeout);
            return (null, $"Timeout after {_opt.HttpTimeout}.");
        }
    }

    private static string Trunc(string? s) =>
        string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= 480 ? s : s[..480]);
}
