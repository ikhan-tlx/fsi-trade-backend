using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Detail;

/// <summary>
/// Slice 10 §7.6 — resolves a flat <c>productSummary</c> object by
/// back-tracing canonical FE field names through
/// <c>TmX_Tenant_Field_Setup</c> and the transaction's
/// <c>UDF_Data</c> JSON blob.
///
/// User direction: "this should be back trace from tenant field setup
/// and udf data" — no admin mapping table, no FE-side string matching;
/// the API does the lookup once and emits a typed sub-object.
///
/// Lookup algorithm
/// ----------------
/// For each canonical name (e.g. <c>"LCReference"</c>) we try a small
/// set of likely <c>FieldName</c> spellings against the product's
/// TenantFieldSetup rows. First match wins. The candidate sets are
/// deliberately generous because the legacy field names vary across
/// imported products (e.g. <c>"LCReference"</c>, <c>"LC_Reference"</c>,
/// <c>"ImportLcRefNum"</c>).
///
/// Once we have a TenantFieldSetupId, we read the corresponding value
/// from <c>UDF_Data</c> at key <c>"tf{id}"</c> (the form runtime's
/// canonical key shape; see <c>writeFieldValue</c> in the wizard).
/// Returns null per slot when nothing matches — the FE renders "—".
/// </summary>
public static class ProductSummaryProjection
{
    /// <summary>
    /// Canonical name → list of likely TmX_Tenant_Field_Setup.Field_Name
    /// spellings. Order matters: first hit wins.
    /// </summary>
    private static readonly (string Canonical, string[] Candidates)[] CanonicalFieldMap =
    {
        ("LCReference",      new[] { "LCReference", "LC_Reference", "LCRefNumber", "LCNo", "ImportLcRefNum" }),
        ("HSCode",           new[] { "HSCode", "HS_Code", "HSCodeValue", "HS" }),
        ("ShipmentMode",     new[] { "ShipmentMode", "Shipment_Mode", "ModeOfShipment", "ShipmentType" }),
        ("Vessel",           new[] { "Vessel", "VesselName", "VesselNo", "VesselNumber" }),
        ("PortOfLoading",    new[] { "PortOfLoading", "Port_of_Loading", "POL", "LoadingPort" }),
        ("PortOfDischarge",  new[] { "PortOfDischarge", "Port_of_Discharge", "POD", "DischargePort" }),
        ("BeneficiaryBank",  new[] { "BeneficiaryBank", "Beneficiary_Bank", "BenBank", "BenefBank" }),
        ("GoodsDescription", new[] { "GoodsDescription", "Goods_Description", "GoodsDesc", "Goods", "ProductDescription" }),
        ("Quantity",         new[] { "Quantity", "Qty", "ShipmentQuantity", "GoodsQuantity" }),
    };

    /// <summary>
    /// Builds the summary for one transaction. Returns null when the
    /// product has no matching fields configured OR <c>udfRaw</c> is empty —
    /// callers can omit the sub-object entirely in those cases.
    /// </summary>
    public static async Task<ProductSummaryDto?> BuildAsync(
        IApplicationDbContext db,
        int                   productId,
        string?               udfRaw,
        CancellationToken     ct)
    {
        if (string.IsNullOrWhiteSpace(udfRaw)) return null;

        // Flatten every candidate name across the canonical map. One IN-list
        // query lets the DB return only the rows we actually care about.
        var allCandidates = CanonicalFieldMap
            .SelectMany(m => m.Candidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fieldRows = await db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId == productId
                    &&  f.FieldName != null
                    &&  allCandidates.Contains(f.FieldName!))
            .Select(f => new { f.TenantFieldSetupId, f.FieldName })
            .ToListAsync(ct);

        if (fieldRows.Count == 0) return null;

        // Index by FieldName (case-insensitive) so candidate-set lookups
        // are O(1). Some products have duplicate rows per (field × locale);
        // first row wins (TenantFieldSetupId order is stable).
        var idByName = fieldRows
            .GroupBy(r => r.FieldName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().TenantFieldSetupId, StringComparer.OrdinalIgnoreCase);

        // Parse UDF blob once, walk it as a JsonDocument.
        using var doc = JsonDocument.Parse(udfRaw);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var summary = new ProductSummaryDto();
        foreach (var (canonical, candidates) in CanonicalFieldMap)
        {
            // Pick the first candidate whose FieldName exists for this product.
            int? matchedId = null;
            foreach (var cand in candidates)
            {
                if (idByName.TryGetValue(cand, out var id))
                {
                    matchedId = id;
                    break;
                }
            }
            if (!matchedId.HasValue) continue;

            // Read UdfData at "tf{id}". Some legacy variants store the
            // payload under just the bare id ("123") — try both.
            var keys = new[] { "tf" + matchedId.Value, matchedId.Value.ToString() };
            string? value = null;
            foreach (var key in keys)
            {
                if (root.TryGetProperty(key, out var element))
                {
                    value = ExtractScalar(element);
                    if (value is not null) break;
                }
            }
            if (value is null) continue;

            AssignSlot(summary, canonical, value);
        }

        return HasAny(summary) ? summary : null;
    }

    /// <summary>
    /// Flattens a JsonElement to a string. Strings come through verbatim;
    /// numbers/booleans get their canonical text form; nulls return null.
    /// Objects/arrays return null — we can't surface complex shapes on
    /// the summary card meaningfully.
    /// </summary>
    private static string? ExtractScalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String   => element.GetString(),
        JsonValueKind.Number   => element.GetRawText(),
        JsonValueKind.True     => "true",
        JsonValueKind.False    => "false",
        JsonValueKind.Null     => null,
        JsonValueKind.Undefined=> null,
        _                      => null
    };

    private static void AssignSlot(ProductSummaryDto target, string canonical, string value)
    {
        switch (canonical)
        {
            case "LCReference":      target.lcReference      = value; break;
            case "HSCode":           target.hsCode           = value; break;
            case "ShipmentMode":     target.shipmentMode     = value; break;
            case "Vessel":           target.vessel           = value; break;
            case "PortOfLoading":    target.portOfLoading    = value; break;
            case "PortOfDischarge":  target.portOfDischarge  = value; break;
            case "BeneficiaryBank":  target.beneficiaryBank  = value; break;
            case "GoodsDescription": target.goodsDescription = value; break;
            case "Quantity":         target.quantity         = value; break;
        }
    }

    private static bool HasAny(ProductSummaryDto s) =>
        !string.IsNullOrWhiteSpace(s.lcReference)
        || !string.IsNullOrWhiteSpace(s.hsCode)
        || !string.IsNullOrWhiteSpace(s.shipmentMode)
        || !string.IsNullOrWhiteSpace(s.vessel)
        || !string.IsNullOrWhiteSpace(s.portOfLoading)
        || !string.IsNullOrWhiteSpace(s.portOfDischarge)
        || !string.IsNullOrWhiteSpace(s.beneficiaryBank)
        || !string.IsNullOrWhiteSpace(s.goodsDescription)
        || !string.IsNullOrWhiteSpace(s.quantity);
}

/// <summary>
/// Flat product-summary card values for the Trade Finance detail page.
/// Each slot is optional — null means the field isn't configured for
/// this product OR the form value hasn't been entered yet. The FE
/// renders "—" for null/empty slots.
/// </summary>
public class ProductSummaryDto
{
    public string? lcReference      { get; set; }
    public string? hsCode           { get; set; }
    public string? shipmentMode     { get; set; }
    public string? vessel           { get; set; }
    public string? portOfLoading    { get; set; }
    public string? portOfDischarge  { get; set; }
    public string? beneficiaryBank  { get; set; }
    public string? goodsDescription { get; set; }
    public string? quantity         { get; set; }
}
