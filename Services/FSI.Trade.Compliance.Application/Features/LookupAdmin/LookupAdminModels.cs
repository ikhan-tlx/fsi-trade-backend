using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.LookupAdmin;

/// <summary>
/// DTOs, the compiled allow-list constant, and the shared "is this type
/// manageable" guard for the Lookup Management module (/admin/lookups).
///
/// See LOOKUP_MANAGEMENT_MODULE_DESIGN_AND_PLAN.md. The module manages only the
/// VALUES of a curated set of <c>TmX_Lookup</c> types — never the types
/// themselves. Property names are camelCase (matching the API's default
/// serialization + the FE contract), mirroring the ReferenceLists feature.
/// </summary>

// ═══════════════ Allow-list (compiled source of truth) ══════════════════════
/// <summary>
/// The single compiled source of truth for "which lookup types may EVER be
/// edited from the UI" (plan §3, Decision #1). A type is only actually
/// manageable if it is BOTH in this set AND referenced by a live field
/// (see <see cref="LookupAdminGuard"/>).
///
/// DECIDED (plan §15 #1): seeded with the full set of DISTINCT
/// <c>Field_Lookup_Type</c> values present in <c>TmX_Tenant_Field_Setup</c>,
/// EXCLUDING <c>ProductTab</c> (a structural/runtime selector with no
/// <c>TmX_Lookup</c> rows to manage).
///
/// This set was seeded from the known bound types. To refresh/extend it against
/// a live DB, run the discovery query below and add any new strings — nothing
/// else changes (the referenced-by-a-field gate hides allow-listed-but-unused
/// types until a field binds to them):
///
///   SELECT  fs.Field_Lookup_Type                         AS LookupType,
///           COUNT(DISTINCT fs.Tenant_Field_Setup_Id)     AS BoundFields,
///           (SELECT COUNT(*) FROM TmX_Lookup l
///             WHERE l.Lookup_Type = fs.Field_Lookup_Type) AS ValueCount
///   FROM    TmX_Tenant_Field_Setup fs
///   WHERE   fs.Field_Lookup_Type IS NOT NULL
///     AND   fs.Field_Lookup_Type <> 'ProductTab'
///   GROUP BY fs.Field_Lookup_Type
///   ORDER BY BoundFields DESC;
/// </summary>
public static class LookupAdminAllowList
{
    // Seeded from the DISTINCT TmX_Tenant_Field_Setup.Field_Lookup_Type values
    // (decision §15-1), EXCLUDING: ProductTab (structural selector); HSPriceList
    // and HSCodeList (huge/structural — HSPriceList legitimately has duplicate
    // (type,parent,code) rows); and every groupLookupBy('HSPriceList', tf…)
    // expression (those are dynamic HS-price grouping directives, not lookup
    // types). Verified via sqlcmd against ICBC_DEMO on 2026-07-20. To refresh:
    //   SELECT DISTINCT Field_Lookup_Type FROM TmX_Tenant_Field_Setup
    //   WHERE Field_Lookup_Type IS NOT NULL AND Field_Lookup_Type NOT LIKE '%(%'
    //     AND Field_Lookup_Type NOT IN ('ProductTab','HSPriceList','HSCodeList');
    public static readonly IReadOnlySet<string> Types =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Nationality", "HSPriceList", "HSCodeList", "TradeNationality", "LOB", "termofpayment",
            "Currency", "Industry", "CustTranThrough", "Port", "ExpiryType",
            "IncotermCust",  "PerfCust", "tradefacilitycustomer",
            "TradeProdFac", "Document", "BusinessStructure", "CustWithlocal",
            "Confirmation", "Province", "Charges", "UndertakeForm", "DoesCustIntend",
            "Delivery", "UnitofMeasure", "ApplicableRule", "FileIdent",
            "ModeOfShipment", "DemandState", "Year",
            "dealingwithDefenseAndLocal", "BranchCode", "Purpose", "CommonUOM",
            "CustomerIndustrySector",
        };

    public static bool Contains(string? lookupType) =>
        !string.IsNullOrWhiteSpace(lookupType) && Types.Contains(lookupType.Trim());

    /// <summary>
    /// Large curated HS catalogues (HS codes + their price bands). Unlike the other
    /// managed types these are NOT bound to a field via <c>Field_Lookup_Type</c> — the
    /// form references them indirectly through <c>groupLookupBy('HSPriceList', …)</c>
    /// expressions — so the "referenced-by-a-field" gate would hide them. They ARE real
    /// TmX_Lookup catalogues the tenant maintains, so they are manageable purely on the
    /// allow-list (both the types list and the read/write guard special-case them).
    /// NOTE: HSPriceList legitimately carries duplicate (type,parent,code) rows, so its
    /// single-value uniqueness check can be conservative — bulk upsert on (type,parent,code)
    /// is the intended maintenance path.
    /// </summary>
    public static readonly IReadOnlySet<string> StructuralTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HSCodeList", "HSPriceList" };

    public static bool IsStructural(string? lookupType) =>
        !string.IsNullOrWhiteSpace(lookupType) && StructuralTypes.Contains(lookupType.Trim());
}

/// <summary>
/// Server-side defence-in-depth: re-derives the managed set and throws
/// <see cref="ForbiddenException"/> (<c>lookup_type_not_manageable</c>, 403) on
/// every read/write of a type that is not both allow-listed AND bound to a
/// field. The FE gate is UX only — this is the security boundary (plan §3, §5).
/// </summary>
public static class LookupAdminGuard
{
    /// <summary>
    /// True if <paramref name="lookupType"/> is manageable — i.e. on the compiled
    /// allow-list (original curated types) OR registered as manageable in
    /// <c>TmX_Lookup_Type</c> (admin-created types; auto-allowed on creation).
    /// </summary>
    public static async Task<bool> IsManageableAsync(IApplicationDbContext db, string? lookupType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(lookupType)) return false;
        if (LookupAdminAllowList.Contains(lookupType)) return true;
        var lt = lookupType.Trim();
        return await db.LookupTypeRegistrations.AsNoTracking()
            .AnyAsync(r => r.LookupType == lt && r.IsManageable, ct);
    }

    /// <summary>Throws 403 <c>lookup_type_not_manageable</c> unless the type is manageable.</summary>
    public static async Task EnsureManageableAsync(IApplicationDbContext db, string? lookupType, CancellationToken ct)
    {
        if (!await IsManageableAsync(db, lookupType, ct))
            throw new ForbiddenException(
                "lookup_type_not_manageable",
                $"The lookup type '{lookupType}' is not manageable from this module.");
    }
}

// ═══════════════ Types ═══════════════════════════════════════════════════════
public class ManagedLookupTypeDto
{
    public string lookupType { get; set; } = "";
    public string displayName { get; set; } = "";
    public string? description { get; set; }
    public int boundFieldCount { get; set; }
    public int valueCount { get; set; }
    public int activeValueCount { get; set; }
    public bool isHierarchical { get; set; }
    public bool editable { get; set; } = true;   // managed types always allow value edits
    public string source { get; set; } = "MANAGED";
    public string status { get; set; } = "Active"; // "Active" | "Inactive"
}

/// <summary>One bound field (product / tab / label) for a managed type's "where used".</summary>
public class LookupTypeUsageDto
{
    public string? productCode { get; set; }
    public string? tabTitle { get; set; }
    public string? fieldLabel { get; set; }
}

// ═══════════════ Values ══════════════════════════════════════════════════════
public class LookupValueDto
{
    public int id { get; set; }
    public string? lookupType { get; set; }
    public string? label { get; set; }   // Visible_Value
    public string? code { get; set; }   // Hidden_Value
    public string? description { get; set; }
    public int? parentId { get; set; }
    public string? parentLabel { get; set; }
    public int? sortOrder { get; set; }
    public bool isActive { get; set; }
    public string? lastUpdatedBy { get; set; }
    public DateTime? lastUpdatedDate { get; set; }
    public int txnUsageCount { get; set; }
    public int ruleUsageCount { get; set; }
    public bool editable { get; set; }
    public bool deletable { get; set; }
}

/// <summary>Detail of where a single value is referenced (drives the delete guard UI).</summary>
public class LookupValueUsageDto
{
    public int txnUsageCount { get; set; }
    public int ruleUsageCount { get; set; }
    public bool inUse { get; set; }
    public List<LookupValueRuleUsageDto> rules { get; set; } = new();
}

public class LookupValueRuleUsageDto
{
    public int flagId { get; set; }
    public string? fieldCode { get; set; }
    public string op { get; set; } = "";
}

/// <summary>
/// The serialized change intent stored in <c>TmX_Request.Entity_Json</c> for a
/// maker-checker Lookup Modification Request (request-type code "0002"). The
/// apply-on-approval step (<see cref="ApplyLookupChangeCommand"/>) deserializes
/// this and re-runs validation before mutating <c>TmX_Lookup</c>.
/// PascalCase so <c>Name</c> is picked up by the Requests grid's entity-name
/// extractor (which reads "Name"/"name").
/// </summary>
public class LookupChangeIntent
{
    public string Operation { get; set; } = "";   // create | update | activate | deactivate | delete | bulk
    public string LookupType { get; set; } = "";
    public int? LookupId { get; set; }
    public string? Label { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public int? SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Human label for the Requests queue grid, e.g. "PEP / Y".</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Only for Operation == "bulk" (template-driven bulk value import). Carries the
    /// VALID rows the maker uploaded (invalid rows were reported and dropped at upload
    /// time). Applied atomically on approval by <see cref="ApplyLookupChangeCommand"/>.
    /// </summary>
    public List<LookupBulkRowIntent>? Rows { get; set; }
}

/// <summary>One value row inside a bulk-import <see cref="LookupChangeIntent"/> (Operation "bulk").</summary>
public class LookupBulkRowIntent
{
    public string Code { get; set; } = "";
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Request-type / entity-status codes shared with the Requests module (FE requests-constants.ts).</summary>
public static class LookupRequestCodes
{
    public const string RequestType_Lookup = "0002";   // "Lookup Modification Request"

    public const string RequestStatus_Pending = "0011"; // New
    public const string RequestStatus_Approved = "0012"; // Approve
    public const string RequestStatus_Rejected = "0013"; // Reject

    public const string EntityStatus_Create = "0001"; // Add
    public const string EntityStatus_Update = "0002"; // Update
    public const string EntityStatus_Delete = "0003"; // Delete
    public const string EntityStatus_Deactivate = "0004"; // Deactivate
}
