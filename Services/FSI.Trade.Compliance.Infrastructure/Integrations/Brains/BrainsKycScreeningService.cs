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
/// Request shape mirrors the production legacy INT call (verified from its
/// int.log):
///   APPLY_NO=001, TASK_NO=&lt;FileTime&gt;, ZONENO=6001, ADJUST_TYPE=1,
///   CUSTNO=&lt;code&gt;, REG_TYPE=, REG_NO=
/// CUSTNO is the per-call customer code; ZONENO/ADJUST_TYPE are configurable
/// defaults; APPLY_NO/TASK_NO are generated per request; REG_TYPE/REG_NO are
/// sent EMPTY (BRAINS keys the lookup on CUSTNO alone).
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
            var parsed  = ParseResponse(payload);

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
    /// Builds the servlet URL. Mirrors the production legacy INT call
    /// (verified from int.log):
    ///   …QueryKycInfoServlet?APPLY_NO=001&amp;TASK_NO=&lt;FileTime&gt;
    ///     &amp;ZONENO=6001&amp;ADJUST_TYPE=1&amp;CUSTNO=&lt;code&gt;
    ///     &amp;REG_TYPE=&amp;REG_NO=
    /// APPLY_NO + TASK_NO are generated per request; REG_TYPE/REG_NO are sent
    /// EMPTY — BRAINS keys the lookup on CUSTNO alone (the optional name /
    /// TELLER / BRNO / REPORT_TYPE / REMARK fields are omitted, as the legacy
    /// sent them blank too).
    /// </summary>
    private string BuildUrl(string customerId)
    {
        // TASK_NO is a generated unique id (legacy used DateTime.ToFileTime,
        // e.g. 134263410405107182). APPLY_NO is the fixed value the legacy
        // INT service sent.
        var taskNo = DateTime.UtcNow.ToFileTime().ToString();

        var qs = string.Join("&", new[]
        {
            "APPLY_NO=001",
            $"TASK_NO={Uri.EscapeDataString(taskNo)}",
            $"ZONENO={Uri.EscapeDataString(_opt.ZoneNo)}",
            $"ADJUST_TYPE={Uri.EscapeDataString(_opt.AdjustType)}",
            $"CUSTNO={Uri.EscapeDataString(customerId)}",
            "REG_TYPE=",
            "REG_NO="
        });

        return $"{_opt.BaseUrl}?{qs}";
    }

    /// <summary>
    /// Deserialize a BRAINS reply. BRAINS sometimes returns single-quoted
    /// pseudo-JSON (e.g. <c>{'STATUS':'','TRANSOK':'1',…}</c>) which
    /// System.Text.Json rejects; we retry once after normalising single
    /// quotes to double quotes. Strict (double-quoted) replies parse on the
    /// first attempt and are unaffected. A genuinely malformed payload still
    /// throws <see cref="JsonException"/>, handled by the caller.
    /// </summary>
    private static BrainsKycResponse? ParseResponse(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<BrainsKycResponse>(payload, JsonOpts);
        }
        catch (JsonException)
        {
            var normalized = payload.Replace('\'', '"');
            return JsonSerializer.Deserialize<BrainsKycResponse>(normalized, JsonOpts);
        }
    }

    /// <summary>Masks the customer identifier in logs — keep only the last 4 chars.</summary>
    private static string Mask(string? id)
        => string.IsNullOrEmpty(id) ? "(none)"
         : id.Length <= 4 ? new string('*', id.Length)
         : new string('*', id.Length - 4) + id[^4..];
}
