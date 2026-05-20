using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>
    /// Submits a case to FCCM. Returns the upstream request ID (their handle
    /// for the in-flight provisioning). Null = submit failed; caller logs the
    /// case as Failed.
    /// </summary>
    public async Task<string?> SubmitCaseAsync(string customerId, long? transactionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.FccmOnboardingUrl))
        {
            _log.LogWarning("FccmOnboardingUrl not configured. SubmitCaseAsync returning null (no-op).");
            return null;
        }

        var payload = new
        {
            CustomerNo    = customerId,
            TransactionId = transactionId
            // Real upstream contract may require many more fields; refine
            // with FCCM team in Slice 4 build phase.
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync(_opt.FccmOnboardingUrl, payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("FCCM submit returned HTTP {Status} for {CustomerId}.", resp.StatusCode, customerId);
                return null;
            }

            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

            // Tolerate either { "requestId": "..." } or { "RequestID": "..." }.
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "requestId", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(prop.Name, "RequestID", StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();
                    }
                }
            }

            _log.LogWarning("FCCM submit response for {CustomerId} contained no requestId field.", customerId);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "FCCM submit HTTP failure for {CustomerId}.", customerId);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogError("FCCM submit HTTP timeout for {CustomerId} after {Timeout}.", customerId, _opt.HttpTimeout);
            return null;
        }
    }
}
