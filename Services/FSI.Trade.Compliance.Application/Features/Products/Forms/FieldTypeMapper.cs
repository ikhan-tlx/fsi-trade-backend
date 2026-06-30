namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Maps the raw <c>TmX_Tenant_Field_Setup.Field_Type_Lkp</c> integer to a
/// stable semantic type string the FE renders + the Field Configuration
/// admin page displays (so the page shows "Dropdown", "Number", "Date" etc.
/// instead of defaulting every row to "text").
///
/// Source of truth: DynamicForm_Integration_Guide.md §5 "Field Type
/// Reference". The <c>ProductTab</c> selector type is NOT a fixed integer —
/// it's the runtime <c>Lookup_ID</c> of the ProductTab row — so it's
/// detected via <c>Field_Lookup_Type == "ProductTab"</c> (per the guide),
/// checked before the numeric switch.
/// </summary>
public static class FieldTypeMapper
{
    /// <summary>Sentinel for the runtime ProductTab lookup type (guide §3.3 / §5).</summary>
    private const string ProductTabLookup = "ProductTab";

    public static string ToSemanticType(int? fieldTypeLkp, string? fieldLookupType)
    {
        // ProductTab is keyed by Field_Lookup_Type, not a fixed Field_Type_Lkp.
        if (string.Equals(fieldLookupType, ProductTabLookup, System.StringComparison.OrdinalIgnoreCase))
            return "productTab";

        return fieldTypeLkp switch
        {
            2  => "date",        // Date picker
            3  => "text",        // Text input
            4  => "textarea",    // Multiline text
            24 => "spacer",      // Visual gap (non-input)
            25 => "select",      // Single-select dropdown
            65 => "searchableSelect", // Type-to-filter dropdown (large lookups, e.g. HS Code)
            26 => "multi",       // Multi-select dropdown
            27 => "repeat",      // Checkbox / RepeatTrigger — repeating table trigger
            28 => "boolean",     // Generic boolean checkbox
            30 => "section",     // Card section header (non-input)
            40 => "tableRoot",   // System root — not rendered
            55 => "number",      // Numeric input
            64 => "endMarker",   // Table boundary marker (non-input)
            _  => "text"         // Safe default for unmapped / null types
        };
    }

    /// <summary>
    /// Resolves the DYNAMIC display name for a field type from the
    /// pre-loaded <c>TmX_Lookup</c> (Lookup_Type = 'field') map — keyed by
    /// <c>Field_Type_Lkp</c>. Returns null when the lookup has no row for the
    /// code (caller falls back to <see cref="ToSemanticType"/>).
    /// </summary>
    public static string? ResolveTypeName(int? fieldTypeLkp, IReadOnlyDictionary<int, string> typeNames)
        => fieldTypeLkp.HasValue && typeNames.TryGetValue(fieldTypeLkp.Value, out var name)
            ? name
            : null;
}
