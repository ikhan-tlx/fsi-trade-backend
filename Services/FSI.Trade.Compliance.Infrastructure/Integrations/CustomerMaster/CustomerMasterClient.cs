using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.CustomerMaster;

/// <summary>
/// HTTP-backed implementation of <see cref="ICustomerMasterService"/>.
/// Today's URL shape mirrors the legacy gateway path
/// <c>{base}/DLP/GetCustomerId/{customerId}</c>; refine when the upstream
/// service contract is finalised.
///
/// Unknown response fields are passed through to <see cref="CustomerMaster.Additional"/>
/// so the FE can surface them without a backend rebuild.
/// </summary>
public class CustomerMasterClient : ICustomerMasterService
{
    private readonly HttpClient                       _http;
    private readonly IntegrationOptions               _opt;
    private readonly ILogger<CustomerMasterClient>    _log;

    public CustomerMasterClient(
        HttpClient                       http,
        IOptions<IntegrationOptions>     opt,
        ILogger<CustomerMasterClient>    log)
    {
        _http = http;
        _opt  = opt.Value;
        _log  = log;
    }

    public async Task<Application.Contracts.Integrations.CustomerMaster?>
        GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.CustomerMasterBaseUrl))
        {
            _log.LogWarning("Customer master base URL not configured. Returning null.");
            return null;
        }

        var url = $"{_opt.CustomerMasterBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(customerId)}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Customer master returned HTTP {Status} for {CustomerId}.", resp.StatusCode, customerId);
                return null;
            }

            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

            return Map(doc.RootElement, customerId);
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "Customer master HTTP failure for {CustomerId}.", customerId);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogError("Customer master HTTP timeout for {CustomerId} after {Timeout}.", customerId, _opt.HttpTimeout);
            return null;
        }
    }

    /// <summary>
    /// Maps an upstream JSON object onto our <see cref="Application.Contracts.Integrations.CustomerMaster"/>.
    /// Known fields populate strongly-typed properties; everything else lands
    /// in <c>Additional</c> as string key/value pairs.
    /// </summary>
    private static Application.Contracts.Integrations.CustomerMaster Map(JsonElement el, string fallbackCustomerId)
    {
        var c = new Application.Contracts.Integrations.CustomerMaster
        {
            CustomerCode = ReadString(el, "CustomerCode") ?? fallbackCustomerId
        };

        c.CustomerName            = ReadString(el, "CustomerName");
        c.CustomerType            = ReadString(el, "CustomerType");
        c.NationalIdentifierType  = ReadString(el, "NationalIdentifierType");
        c.NationalIdentifierValue = ReadString(el, "NationalIdentifierValue");
        c.EmailAddress            = ReadString(el, "EmailAddress");
        c.PhoneNumber             = ReadString(el, "PhoneNumber");
        c.AddressLine1            = ReadString(el, "AddressLine1");
        c.AddressLine2            = ReadString(el, "AddressLine2");
        c.City                    = ReadString(el, "City");
        c.Country                 = ReadString(el, "Country");
        c.LocationId              = ReadInt   (el, "LocationId");
        c.BranchCode              = ReadString(el, "BranchCode");
        c.BranchName              = ReadString(el, "BranchName");
        c.Status                  = ReadString(el, "Status");
        c.RegistrationDate        = ReadDate  (el, "RegistrationDate");

        // Anything we didn't pluck above goes into Additional verbatim, so
        // the FE can pick up new upstream fields without a backend release.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CustomerCode","CustomerName","CustomerType","NationalIdentifierType","NationalIdentifierValue",
            "EmailAddress","PhoneNumber","AddressLine1","AddressLine2","City","Country","LocationId",
            "BranchCode","BranchName","Status","RegistrationDate"
        };
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (known.Contains(prop.Name)) continue;
                c.Additional[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : prop.Value.ToString();
            }
        }

        return c;
    }

    private static string?   ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
    private static int?      ReadInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;
    private static DateTime? ReadDate(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetDateTime(out var d) ? d : null;
}
