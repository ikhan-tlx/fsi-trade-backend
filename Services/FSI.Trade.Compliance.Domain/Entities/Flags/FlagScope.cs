namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// "Which product / tab carries this flag". Maps to
/// <c>dbo.TmX_Flag_Scope</c>.
///
/// One row per source <c>TmX_Tenant_Field_Setup</c> MRL row. The same
/// <see cref="FlagId"/> appears under multiple (Product, Tab)
/// combinations — that's how the seed migration de-duplicates the
/// indicator text while preserving every form-rendering location.
///
/// <c>TabId</c> is nullable for product-level scopes (no tab
/// discriminator); today every legacy MRL row has a real Tab_ID so the
/// nullable column is forward-looking.
///
/// <see cref="LegacyFieldName"/> preserves the original "ILFMRL5"-style
/// identifier for forensic traceability and for the UDF_Data backfill
/// path resolution.
/// </summary>
public class FlagScope
{
    public int       FlagScopeId           { get; set; }
    public int       FlagId                { get; set; }
    public int       ProductId             { get; set; }
    public int?      TabId                 { get; set; }
    public int       SortOrder             { get; set; }

    /// <summary>
    /// Per-scope active flag. Source rows with Visibility='0' migrated
    /// as ActiveFlag=false. The parent <see cref="FlagCatalogue.ActiveFlag"/>
    /// is independent — a flag can be active on one product and
    /// inactive on another.
    /// </summary>
    public bool      ActiveFlag            { get; set; } = true;

    /// <summary>Original TmX_Tenant_Field_Setup.Field_Name (e.g. "ILFMRL5").</summary>
    public string?   LegacyFieldName       { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}
