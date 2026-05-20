namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// Master flag definition. Maps to <c>dbo.TmX_Flag_Catalogue</c>
/// (Slice 8 Step 1).
///
/// One row per distinct indicator text. The same flag (e.g. "The
/// packaging of goods is inconsistent...") collapses into a single
/// catalogue entry regardless of how many product / tab / subtype
/// variants existed in the legacy <c>TmX_Tenant_Field_Setup</c> form
/// definition. The "where does it apply" dimension is on <see cref="FlagScope"/>.
///
/// Lookup-driven for type / category / severity so adding a new
/// taxonomy member is a data change rather than a code change.
/// </summary>
public class FlagCatalogue
{
    public int       FlagId                { get; set; }

    /// <summary>
    /// Stable code, e.g. <c>"TBML.MRL.C8CE048A"</c>. Built by the seed
    /// migration as <c>{category}.MRL.{sha1-prefix}</c>. Used by
    /// integrations + APIs that want a string handle.
    /// </summary>
    public string    FlagCode              { get; set; } = "";

    /// <summary>Short label for grids / dropdowns (first ~100 chars of description).</summary>
    public string    FlagName              { get; set; } = "";

    /// <summary>Full analyst-facing indicator text.</summary>
    public string    FlagDescription       { get; set; } = "";

    /// <summary>FK to TmX_Lookup row with Lookup_Type='FLAG_TYPE' (Manual / Automated).</summary>
    public int       FlagTypeLkpId         { get; set; }

    /// <summary>FK to TmX_Lookup row with Lookup_Type='FLAG_CATEGORY' (TBML / KYC / Onboarding / Generic).</summary>
    public int?      FlagCategoryLkpId     { get; set; }

    /// <summary>FK to TmX_Lookup row with Lookup_Type='FLAG_SEVERITY' (Critical / High / Medium / Low / Info).</summary>
    public int?      SeverityLkpId         { get; set; }

    /// <summary>Risk-score contribution. Default 1.00; admin tunes via the catalogue UI.</summary>
    public decimal   DefaultWeight         { get; set; } = 1.00m;

    /// <summary>Whether an evidence attachment is required when this flag is set.</summary>
    public bool      RequiresEvidence      { get; set; }

    /// <summary>NULL = manual / analyst-set. Non-NULL = origin upstream system (BRAINS, FCCM, etc.).</summary>
    public string?   SourceSystem          { get; set; }

    public bool      ActiveFlag            { get; set; } = true;

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}
