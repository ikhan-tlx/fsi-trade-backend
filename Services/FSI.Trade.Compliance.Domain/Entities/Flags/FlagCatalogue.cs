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

    // Flag-level category removed 2026-06-01 (Slice 8.2): categorisation now
    // lives on the flag SCOPE (product / tab), so a catalogue-level category
    // was redundant. Column dropped by migration 2026_06_032.

    /// <summary>FK to TmX_Lookup row with Lookup_Type='FLAG_SEVERITY' (Critical / High / Medium / Low / Info).</summary>
    public int?      SeverityLkpId         { get; set; }

    /// <summary>Risk-score contribution. Default 1.00; admin tunes via the catalogue UI.</summary>
    public decimal   DefaultWeight         { get; set; } = 1.00m;

    /// <summary>Whether an evidence attachment is required when this flag is set.</summary>
    public bool      RequiresEvidence      { get; set; }

    /// <summary>
    /// Auto-flag engine behaviour (2026_06_061). FALSE (default) = AGGREGATE:
    /// one TmX_Transaction_Flag row per (txn, flag), Matched_Value summarises all
    /// matches, one FCCM event. TRUE = PER-MATCH: one row per matching
    /// (field × value) — each sanctioned country / good raises its own flag and
    /// FCCM event (legacy "flag every field" parity). Ignored for manual flags.
    /// </summary>
    public bool      RaisePerMatch         { get; set; }

    /// <summary>NULL = manual / analyst-set. Non-NULL = origin upstream system (BRAINS, FCCM, etc.).</summary>
    public string?   SourceSystem          { get; set; }

    public bool      ActiveFlag            { get; set; } = true;

    /// <summary>
    /// TRUE = shipped/seeded flag. Built-ins can be relabelled and
    /// deactivated but NOT deleted (the admin UI blocks delete; the
    /// delete handler enforces it server-side). FALSE = admin-created.
    /// Backfilled to TRUE for the Slice-8 seed rows by migration 2026_06_031.
    /// </summary>
    public bool      IsBuiltIn             { get; set; }

    /// <summary>Soft-delete tombstone. Excluded from every catalogue read. History rows are preserved.</summary>
    public bool      IsDeleted             { get; set; }
    public string?   DeletedBy             { get; set; }
    public DateTime? DeletedDate           { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }

    // ---- Auto-flag maker-checker lifecycle (2026_06_059) ----
    /// <summary>Draft | PendingApproval | Active | Retired. Null on legacy manual rows.</summary>
    public string?   Status                { get; set; }
    public int?      VersionNo             { get; set; }
    public string?   SubmittedBy           { get; set; }
    public DateTime? SubmittedDate         { get; set; }
    public string?   ApprovedBy            { get; set; }
    public DateTime? ApprovedDate          { get; set; }
    public string?   ChangeReason          { get; set; }
}
