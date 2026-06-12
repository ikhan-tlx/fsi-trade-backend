using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Detail;

/// <summary>
/// Resolves the destination tab id of the sub-product the user picked
/// when creating a transaction, by reconciling the transaction's
/// <c>UDF_Data</c> against the product's ProductTab-typed
/// <c>TenantFieldSetup</c> rows.
///
/// <para>
/// Background. Each Trade product (Import / Export / Guarantee) has a
/// "selector tab" — a tab containing one checkbox per sub-product
/// (Import LC Issuance, Import Contract Registration, …). The
/// checkboxes are tagged with <c>FieldLookupType = "ProductTab"</c>
/// (migration 20260522_001) and carry <c>UDF_Data = {"TabId":N}</c>
/// pointing at the destination sub-product tab. When the user picks
/// one, the wizard writes <c>tf{checkboxFieldId} = true</c> into the
/// transaction's UDF_Data and switches the form view to the
/// destination tab.
/// </para>
///
/// <para>
/// On read, the FE needs the destination tab id again so it can
/// lazy-load exactly that tab's fields without walking the whole
/// catalogue. This projection does it server-side:
/// <list type="number">
///   <item>Pull every active ProductTab-typed TenantFieldSetup row for
///         the product (one IN-list query).</item>
///   <item>Parse the transaction's UDF_Data once.</item>
///   <item>Find the first <c>tf{id} = true</c> key whose id matches a
///         ProductTab field, then return that field's UDF_Data parsed
///         for <c>{"TabId":N}</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// Returns null when the product has no ProductTab fields (KYC and
/// similar single-flow products) OR no sub-product checkbox is set to
/// true yet (draft transaction).
/// </para>
/// </summary>
public static class SelectedSubProductProjection
{
    public static async Task<int?> ResolveAsync(
        IApplicationDbContext db,
        int                   productId,
        string?               udfRaw,
        CancellationToken     ct)
    {
        if (string.IsNullOrWhiteSpace(udfRaw)) return null;

        // Pull every active ProductTab-typed field for this product. The
        // result is the small set of selector checkboxes we'll match
        // against. UdfData on each row carries "{"TabId":N}".
        var productTabFields = await db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId       == productId
                    &&  f.FieldLookupType == "ProductTab"
                    &&  f.IsActive
                    &&  f.UdfData         != null)
            .Select(f => new { f.TenantFieldSetupId, f.UdfData })
            .ToListAsync(ct);

        if (productTabFields.Count == 0) return null;

        // Index the selector field ids for O(1) lookup while scanning UDF.
        var fieldIdToUdf = productTabFields.ToDictionary(
            f => f.TenantFieldSetupId,
            f => f.UdfData);

        // Parse the transaction UDF blob.
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(udfRaw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            // Clone the root element so it survives the using scope.
            root = JsonSerializer.Deserialize<JsonElement>(udfRaw);
        }
        catch (JsonException)
        {
            return null;
        }

        // Walk the transaction UDF for tf{id} = true keys whose id maps to
        // a ProductTab field. First match wins (the FE enforces single-
        // select on the wizard side; if data somehow has multiple, we
        // return whichever comes first in document order).
        foreach (var prop in root.EnumerateObject())
        {
            if (!IsCheckboxTrue(prop.Value)) continue;
            if (!TryParseTfId(prop.Name, out var tfId)) continue;
            if (!fieldIdToUdf.TryGetValue(tfId, out var fieldUdf)) continue;

            var destTabId = ExtractTabId(fieldUdf);
            if (destTabId.HasValue) return destTabId;
        }

        return null;
    }

    /// <summary>
    /// Parses "tf123" → 123. False on anything else (plain numeric keys,
    /// other prefixes, malformed input). The wizard's writeFieldValue
    /// always uses the "tf{id}" shape per FE_CHANGES_REQUIRED.md.
    /// </summary>
    private static bool TryParseTfId(string key, out int id)
    {
        id = 0;
        if (string.IsNullOrEmpty(key) || key.Length < 3) return false;
        if (key[0] != 't' || key[1] != 'f') return false;
        return int.TryParse(key.AsSpan(2), out id);
    }

    /// <summary>
    /// Treats both JSON true and the literal strings "true" / "1" as set.
    /// Legacy rows sometimes wrote booleans as strings; defensive parse.
    /// </summary>
    private static bool IsCheckboxTrue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True   => true,
        JsonValueKind.Number => v.TryGetInt32(out var i) && i == 1,
        JsonValueKind.String => string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase)
                              || v.GetString() == "1",
        _                    => false
    };

    /// <summary>
    /// Reads {"TabId":N} (or "tabId" lowercase variant) from a UDF JSON
    /// blob. Null on absence / malformed JSON / non-integer values.
    /// </summary>
    private static int? ExtractTabId(string? udfRaw)
    {
        if (string.IsNullOrWhiteSpace(udfRaw)) return null;

        try
        {
            using var doc = JsonDocument.Parse(udfRaw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "TabId", StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Value.ValueKind == JsonValueKind.Number
                    && prop.Value.TryGetInt32(out var n)) return n;
                if (prop.Value.ValueKind == JsonValueKind.String
                    && int.TryParse(prop.Value.GetString(), out var ns)) return ns;
            }
        }
        catch (JsonException)
        {
            // Field's UDF_Data malformed — silently skip; the FE will
            // fall back to its full-product load path.
        }

        return null;
    }
}
