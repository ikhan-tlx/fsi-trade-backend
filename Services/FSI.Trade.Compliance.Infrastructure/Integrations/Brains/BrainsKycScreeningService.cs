using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Brains;

/// <summary>
/// BRAINS-backed <see cref="IKycScreeningService"/> for the v2.0 real-time
/// interface (<c>QueryKycInfoServlet</c>). Issues a GET with ONLY the
/// mandatory query params and maps the JSON response via
/// <see cref="BrainsResponseMapper"/>.
///
/// Mandatory request fields (interface doc v2.0):
///   TASK_NO, ZONENO, ADJUST_TYPE, CUSTNO, REG_TYPE, REG_NO
/// All except CUSTNO (the customer code) come from <see cref="BrainsOptions"/>
/// configurable defaults; TASK_NO falls back to the customer code.
///
/// Sandbox mode is a SEPARATE implementation (<see cref="SandboxKycScreeningService"/>)
/// selected by DI when <c>Integration:Brains:Mode = Sandbox</c> — this class
/// is only registered for Live.
/// </summary>
public class BrainsKycScreeningService : IKycScreeningService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient                         _http;
    private readonly BrainsOptions                      _opt;
    private readonly ILogger<BrainsKycScreeningService> _log;

    public BrainsKycScreeningService(
        HttpClient                          http,
        IOptions<BrainsOptions>             opt,
        ILogger<BrainsKycScreeningService>  log)
    {
        _http = http;
        _opt  = opt.Value;
        _log  = log;
    }

    public async Task<KycResult?> GetKycForCustomerAsync(string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.BaseUrl))
        {
            _log.LogWarning("BRAINS BaseUrl not configured (Integration:Brains:BaseUrl). Returning null.");
            return null;
        }

        var url = BuildUrl(customerId);

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("BRAINS returned HTTP {Status} for customer {Cust}.", resp.StatusCode, Mask(customerId));
                return null;
            }

            var payload = await resp.Content.ReadAsStringAsync(ct);
            var parsed  = JsonSerializer.Deserialize<BrainsKycResponse>(payload, JsonOpts);

            // TRANSOK != "0" bundles not-found / multiple / error — log the
            // detail but return null (no usable screening result).
            if (parsed is null || !BrainsResponseMapper.IsSuccess(parsed.TransOk))
            {
                _log.LogWarning("BRAINS no result for customer {Cust} (TRANSOK={TransOk}, ERROR_NO={ErrNo}, ERROR_MSG={ErrMsg}).",
                    Mask(customerId), parsed?.TransOk, parsed?.ErrorNo, parsed?.ErrorMsg);
                return null;
            }

            var result = BrainsResponseMapper.ToKycResult(parsed);
            if (result is null)
                _log.LogWarning("BRAINS returned an unrecognised STATUS '{Status}' for customer {Cust}.",
                    parsed.Status, Mask(customerId));
            return result;
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "BRAINS response was not valid JSON for customer {Cust}.", Mask(customerId));
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "BRAINS HTTP failure for customer {Cust}.", Mask(customerId));
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogError("BRAINS HTTP timeout for customer {Cust}.", Mask(customerId));
            return null;
        }
    }

    /// <summary>
    /// Builds the servlet URL with the six mandatory params only. Optional
    /// fields (TELLER, BRNO, names, REPORT_TYPE, REMARK*) are deliberately
    /// omitted per FSI direction (send mandatory only).
    /// </summary>
    private string BuildUrl(string customerId)
    {
        var taskNo = string.IsNullOrWhiteSpace(_opt.TaskNo) ? customerId : _opt.TaskNo!;

        var qs = string.Join("&", new[]
        {
            $"TASK_NO={Uri.EscapeDataString(taskNo)}",
            $"ZONENO={Uri.EscapeDataString(_opt.ZoneNo)}",
            $"ADJUST_TYPE={Uri.EscapeDataString(_opt.AdjustType)}",
            $"CUSTNO={Uri.EscapeDataString(customerId)}",
            $"REG_TYPE={Uri.EscapeDataString(_opt.RegType)}",
            $"REG_NO={Uri.EscapeDataString(_opt.RegNo)}"
        });

        return $"{_opt.BaseUrl}?{qs}";
    }

    /// <summary>Masks the customer identifier in logs — keep only the last 4 chars.</summary>
    private static string Mask(string? id)
        => string.IsNullOrEmpty(id) ? "(none)"
         : id.Length <= 4 ? new string('*', id.Length)
         : new string('*', id.Length - 4) + id[^4..];
}
