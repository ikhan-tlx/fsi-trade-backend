using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.CustomerMaster;

/// <summary>
/// Slice 11 P0 §1 — local-DB-backed implementation of
/// <see cref="ICustomerMasterService"/>. Reads from
/// <c>TmX_Customer_Master</c> (the per-transaction customer-snapshot
/// table) rather than proxying to BRAINS over HTTP.
///
/// Why this exists
/// ---------------
/// Per <c>BE_revamp_Changes.md §1</c>: the FE wizard's customer lookup
/// behaves differently per product family:
/// <list type="bullet">
///   <item><b>KYC product</b> → customer doesn't exist locally yet,
///         BE proxies to BRAINS via <c>GET /Customer/{id}/Kyc</c>
///         (unchanged; still served by <c>BrainsKycScreeningService</c>).</item>
///   <item><b>Trade product</b> → customer must already have a KYC
///         record on file. <c>GET /Customer/{id}</c> looks them up
///         locally and returns null (FE shows "Customer does not have
///         any KYC Transaction") if no snapshot exists.</item>
/// </list>
///
/// Lookup semantics
/// ----------------
/// The same Customer_Code can appear many times in
/// <c>TmX_Customer_Master</c> (one row per transaction). We return the
/// MOST RECENT snapshot — that's the freshest version of the customer's
/// attributes the bank has on file.
///
/// UDF_Data passthrough
/// --------------------
/// Snapshots carry a UDF JSON blob with whatever extra fields the form
/// captured (address, phone, country, etc.). We parse it server-side
/// and lift well-known keys into the typed properties, then dump any
/// leftover keys into <see cref="CustomerMaster.Additional"/> so the FE
/// can surface them without a backend rebuild.
/// </summary>
public class LocalCustomerMasterReader : ICustomerMasterService
{
    private readonly IApplicationDbContext              _db;
    private readonly ILogger<LocalCustomerMasterReader> _log;

    public LocalCustomerMasterReader(
        IApplicationDbContext              db,
        ILogger<LocalCustomerMasterReader> log)
    {
        _db  = db;
        _log = log;
    }

    public async Task<Application.Contracts.Integrations.CustomerMaster?>
        GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return null;

        var trimmed = customerId.Trim();

        // Most-recent snapshot per Customer_Code. The same logical customer
        // can have many snapshots — one per transaction — and we want the
        // latest one because it reflects the bank's freshest view.
        var snapshot = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.CustomerCode == trimmed)
            .OrderByDescending(c => c.CreatedDate)
            .ThenByDescending(c => c.CustomerMasterId)
            .Select(c => new
            {
                c.CustomerCode,
                c.CustomerName,
                c.CustomerTitle,
                c.NationalIdentifierValue,
                c.NationalIdTypeLkp,
                c.CustomerTypeLkp,
                c.CustomerStatusLkp,
                c.LocationId,
                c.UdfData,
                c.CreatedDate
            })
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            _log.LogInformation(
                "Customer {CustomerId} not found in TmX_Customer_Master — returning null.", trimmed);
            return null;
        }

        var dto = new Application.Contracts.Integrations.CustomerMaster
        {
            CustomerCode            = snapshot.CustomerCode ?? trimmed,
            CustomerName            = snapshot.CustomerName,
            NationalIdentifierValue = snapshot.NationalIdentifierValue,
            // CustomerTypeLkp / NationalIdTypeLkp / CustomerStatusLkp are
            // lookup IDs — surfacing as integer codes lets the FE join
            // against the Lookup catalogue rather than us joining here.
            CustomerType            = snapshot.CustomerTypeLkp?.ToString(),
            NationalIdentifierType  = snapshot.NationalIdTypeLkp?.ToString(),
            Status                  = snapshot.CustomerStatusLkp?.ToString(),
            LocationId              = snapshot.LocationId,
            RegistrationDate        = snapshot.CreatedDate
        };

        // Lift UDF_Data scalar keys into typed slots when they match (e.g.
        // Country, City, AddressLine1, EmailAddress, PhoneNumber); the
        // rest land in Additional so the FE can pick up form-side custom
        // attributes without an API rebuild.
        ApplyUdfData(dto, snapshot.UdfData);

        return dto;
    }

    /// <summary>
    /// Walks the UDF JSON, populating well-known typed slots and dumping
    /// everything else into <c>Additional</c>. Safe against malformed
    /// JSON — falls back to an empty Additional if parse fails.
    /// </summary>
    private void ApplyUdfData(
        Application.Contracts.Integrations.CustomerMaster dto,
        string? udfRaw)
    {
        if (string.IsNullOrWhiteSpace(udfRaw)) return;

        try
        {
            using var doc = JsonDocument.Parse(udfRaw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();

                // Map a handful of common form-field names to typed slots.
                // Case-insensitive — UDF keys are tf{id} mostly, but
                // legacy snapshots sometimes use the field name verbatim.
                switch (prop.Name.ToLowerInvariant())
                {
                    case "country":          dto.Country      = value; break;
                    case "city":             dto.City         = value; break;
                    case "addressline1":     dto.AddressLine1 = value; break;
                    case "addressline2":     dto.AddressLine2 = value; break;
                    case "emailaddress":
                    case "email":            dto.EmailAddress = value; break;
                    case "phonenumber":
                    case "phone":            dto.PhoneNumber  = value; break;
                    case "branchcode":       dto.BranchCode   = value; break;
                    case "branchname":       dto.BranchName   = value; break;
                    default:
                        // Pass-through. Skip if value is null/whitespace —
                        // empty slots add noise to the FE response.
                        if (!string.IsNullOrWhiteSpace(value))
                            dto.Additional[prop.Name] = value;
                        break;
                }
            }
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex,
                "Failed to parse TmX_Customer_Master.UDF_Data for customer {CustomerCode}.",
                dto.CustomerCode);
        }
    }
}
