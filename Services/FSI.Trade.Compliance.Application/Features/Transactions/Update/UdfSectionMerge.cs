using System.Text.Json;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Update;

/// <summary>
/// Pure helpers for routing the FE's dynamic-form field sections into entity
/// <c>UDF_Data</c> columns. The FE sends one object per <c>Field_Table_Name</c>
/// (TmxTransactionDetail, TmxCustomerMaster, …) as a top-level property; these
/// land on <see cref="UpdateTransactionCommand.sections"/> via JsonExtensionData.
///
/// Kept side-effect-free (no EF, no DI) so the merge semantics are unit-testable
/// in isolation — the handler is otherwise awkward to test because it ends by
/// re-querying through the mediator.
/// </summary>
public static class UdfSectionMerge
{
    /// <summary>
    /// Case-insensitively pulls a section by Field_Table_Name and returns it only
    /// when it's a JSON object (the field-name → value bag). Null/array/scalar
    /// values are treated as "absent" so callers never overwrite UDF with junk.
    /// </summary>
    public static JsonElement? TryGetSection(IReadOnlyDictionary<string, JsonElement>? sections, string name)
    {
        if (sections is null) return null;
        foreach (var kv in sections)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)
                && kv.Value.ValueKind == JsonValueKind.Object)
                return kv.Value;
        }
        return null;
    }

    /// <summary>
    /// Merges an incoming section object's keys over the existing UDF JSON and
    /// returns the re-serialized result. Merge (not replace) preserves keys the
    /// FE didn't send this round (e.g. server-managed RiskScore on the customer).
    /// Malformed existing JSON is treated as empty rather than throwing.
    /// </summary>
    public static string Merge(string? existingJson, JsonElement incoming)
    {
        var bag = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson);
                if (existing is not null)
                    foreach (var kv in existing) bag[kv.Key] = kv.Value;
            }
            catch (JsonException) { /* legacy / malformed UDF — start from incoming only */ }
        }
        if (incoming.ValueKind == JsonValueKind.Object)
            foreach (var prop in incoming.EnumerateObject()) bag[prop.Name] = prop.Value.Clone();

        return JsonSerializer.Serialize(bag);
    }

    /// <summary>
    /// Builds the transaction-detail UDF blob the legacy way: the flat
    /// <c>TmxTransactionDetail</c> fields at the ROOT, plus each long-tail
    /// grid/repeating section NESTED under its own key (verbatim — object or
    /// array). Existing keys not resent this round are preserved; a legacy flat
    /// <c>udfData</c> object is merged at the root as a back-compat fallback when
    /// no explicit section is sent. Returns the existing JSON unchanged when
    /// there is nothing to apply (so we never null out persisted UDF).
    /// </summary>
    public static string? MergeDetail(
        string? existingJson,
        JsonElement? flatSection,
        object? legacyFlat,
        IReadOnlyList<KeyValuePair<string, JsonElement>>? nestedSections)
    {
        var hasFlat = flatSection.HasValue && flatSection.Value.ValueKind == JsonValueKind.Object;
        var hasNested = nestedSections is { Count: > 0 };
        var hasLegacy = legacyFlat is not null;

        if (!hasFlat && !hasNested && !hasLegacy) return existingJson; // nothing to change — preserve

        var bag = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson);
                if (existing is not null)
                    foreach (var kv in existing) bag[kv.Key] = kv.Value;
            }
            catch (JsonException) { /* malformed legacy blob — start clean */ }
        }

        // Legacy flat object (only when no explicit section) — merge keys at root.
        if (hasLegacy && !hasFlat)
        {
            try
            {
                var legacyEl = JsonSerializer.SerializeToElement(legacyFlat);
                if (legacyEl.ValueKind == JsonValueKind.Object)
                    foreach (var p in legacyEl.EnumerateObject()) bag[p.Name] = p.Value.Clone();
            }
            catch (JsonException) { /* ignore unserializable legacy payload */ }
        }

        // Flat transaction fields at the root.
        if (hasFlat)
            foreach (var p in flatSection!.Value.EnumerateObject()) bag[p.Name] = p.Value.Clone();

        // Long-tail grid/repeating sections nested under their own key.
        if (hasNested)
            foreach (var s in nestedSections!) bag[s.Key] = s.Value.Clone();

        return JsonSerializer.Serialize(bag);
    }
}
