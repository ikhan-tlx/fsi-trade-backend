namespace FSI.Trade.Compliance.Application.Features.FieldSetup.Common;

/// <summary>
/// Single source of truth for which <c>Field_Type_Lkp</c> conversions the
/// Field Configuration admin page (Builder — Option A) may perform.
///
/// Type is stored ONLY as <c>Field_Type_Lkp</c> on
/// <c>TmX_Tenant_Field_Setup</c> — both <c>fieldType</c> and
/// <c>fieldTypeName</c> on the read DTO are DERIVED from that integer at read
/// time (see <c>FieldTypeMapper</c>), so "change type" is just swapping this
/// one code. But only within a SAFE family that can't strand stored data or
/// drop a lookup binding.
///
/// Per the field-type reference (DynamicForm_Integration_Guide.md §5) the only
/// genuinely interchangeable input pair in this schema is
/// <b>Text (3) ↔ Textarea / "Long text" (4)</b>: both are free-text inputs,
/// neither carries a lookup binding, and they share identical storage.
/// Everything else (lookups 25/26, Date 2, Number 55, Table 27, Section 30, …)
/// is platform-managed here — the guide defines no distinct "Currency" or
/// "Sub-text" code, so those design "families" collapse to no-ops on real data.
///
/// The map is intentionally tiny and explicit; widen it only when a target is
/// proven safe (same storage class, no lookup binding). The FE mirrors this
/// matrix for the inspector's "family" options; the BE is the enforcer —
/// <c>PublishSection</c> re-validates every requested change server-side.
/// </summary>
public static class FieldTypeSafeMatrix
{
    public const int Text     = 3;
    public const int Textarea = 4;   // rendered as "Long text"

    // currentLkp -> the set of lkp codes it may convert to (incl. itself).
    private static readonly IReadOnlyDictionary<int, HashSet<int>> _safe =
        new Dictionary<int, HashSet<int>>
        {
            [Text]     = new HashSet<int> { Text, Textarea },
            [Textarea] = new HashSet<int> { Text, Textarea },
        };

    /// <summary>True when a field of this type can be retyped at all through
    /// the admin page (i.e. it has more than one safe target).</summary>
    public static bool IsConvertible(int? currentLkp)
        => currentLkp.HasValue
        && _safe.TryGetValue(currentLkp.Value, out var t)
        && t.Count > 1;

    /// <summary>True when converting current → target is in the safe family.</summary>
    public static bool IsSafeConversion(int? currentLkp, int targetLkp)
        => currentLkp.HasValue
        && _safe.TryGetValue(currentLkp.Value, out var t)
        && t.Contains(targetLkp);

    /// <summary>Safe target codes for a field (includes itself); empty when the
    /// type is platform-managed / not convertible.</summary>
    public static IReadOnlyCollection<int> SafeTargets(int? currentLkp)
        => currentLkp.HasValue && _safe.TryGetValue(currentLkp.Value, out var t)
            ? t
            : System.Array.Empty<int>();

    /// <summary>
    /// A non-empty, non-literal-bool <c>Is_Mandatory</c> / <c>Visibility</c> /
    /// <c>IsDisabled</c> string is a client-side formula (e.g.
    /// <c>"tf204 == '1'"</c>) → the field is platform-managed and must NOT be
    /// retyped or have its Required flag clobbered via the admin page.
    /// </summary>
    public static bool IsFormula(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return false;
        var t = expr.Trim();
        if (string.Equals(t, "true",  System.StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(t, "false", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (t == "1" || t == "0") return false;
        return true; // anything else is treated as a formula expression
    }

    /// <summary>True when none of the field's three expression columns carry a
    /// formula — i.e. it is safe to retype.</summary>
    public static bool IsPlatformManaged(string? isMandatory, string? visibility, string? isDisabled)
        => IsFormula(isMandatory) || IsFormula(visibility) || IsFormula(isDisabled);
}
