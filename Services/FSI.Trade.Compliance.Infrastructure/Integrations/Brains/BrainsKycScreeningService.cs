using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Brains;

/// <summary>
/// BRAINS-backed implementation of <see cref="IKycScreeningService"/>. HTTP
/// GET to <c>BrainsKycBaseUrl</c>; parses BRAINS' pipe-delimited
/// <c>STATUS</c> field per the legacy <c>IcbcService.GetKYC</c> behaviour.
///
/// Legacy parsing (tmx-finance-integrations / IcbcService.cs:200-206):
///   STATUS = "Low|John Smith"  →  { RiskScore = "Low", CustomerName = "John Smith" }
///
/// We retain the same parser to match the upstream contract until we have
/// time to negotiate a cleaner JSON shape with the BRAINS team.
/// </summary>
public class BrainsKycScreeningService : IKycScreeningService
{
    private readonly HttpClient                            _http;
    private readonly IntegrationOptions                    _opt;
    private readonly ILogger<BrainsKycScreeningService>    _log;

    public BrainsKycScreeningService(
        HttpClient                          http,
        IOptions<IntegrationOptions>        opt,
        ILogger<BrainsKycScreeningService>  log)
    {
        _http = http;
        _opt  = opt.Value;
        _log  = log;
    }

    public async Task<KycResult?> GetKycForCustomerAsync(string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.BrainsKycBaseUrl))
        {
            _log.LogWarning("BRAINS base URL not configured (Integration:BrainsKycBaseUrl). Returning null.");
            return null;
        }

        // Legacy URL form (IcbcService.cs):
        //   {BRAINSKYCUrl}?APPLY_NO=&CUSTNO={id}
        // The exact querystring may vary by deployment; mirror what's in
        // legacy until we have an updated contract.
        var url = $"{_opt.BrainsKycBaseUrl.TrimEnd('/')}?CUSTNO={Uri.EscapeDataString(customerId)}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("BRAINS returned HTTP {Status} for customer {CustomerId}.", resp.StatusCode, customerId);
                return null;
            }

            var payload = await resp.Content.ReadAsStringAsync(ct);
            return ParseLegacyStatus(payload);
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "BRAINS HTTP failure for customer {CustomerId}.", customerId);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogError("BRAINS HTTP timeout for customer {CustomerId} after {Timeout}.", customerId, _opt.HttpTimeout);
            return null;
        }
    }

    /// <summary>
    /// Pipe-delimited STATUS extractor matching the legacy parser. Tolerates
    /// JSON wrappers like <c>{ "STATUS": "Low|John Smith" }</c> as well as
    /// raw strings.
    /// </summary>
    internal static KycResult? ParseLegacyStatus(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        string statusValue = payload.Trim();

        // Try JSON-wrapped first.
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("STATUS", out var s)
                && s.ValueKind == JsonValueKind.String)
            {
                statusValue = s.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // Not JSON; treat the whole payload as the STATUS string.
        }

        var parts = statusValue.Split('|');
        if (parts.Length == 0) return null;

        return new KycResult
        {
            RiskScore    = parts.Length > 0 ? parts[0].Trim() : "",
            CustomerName = parts.Length > 1 ? parts[1].Trim() : null
        };
    }
}
