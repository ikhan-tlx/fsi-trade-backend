using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Create;

/// <summary>
/// Slice 8 Step 6 — create a new catalogue entry. Optional Flag_Code:
/// if omitted, the handler auto-generates from a category prefix and a
/// SHA-1 hash of the description (matches the seed migration's format
/// so admin-created flags coexist cleanly with backfilled ones).
///
/// Maps to <c>POST /api/v1/Flag</c>. Gated by <c>Flags.Manage</c>.
///
/// The new flag starts unscoped — to deploy it to a product the admin
/// follows up with <c>POST /api/v1/Flag/{id}/Scopes</c>.
/// </summary>
public class CreateFlagCommand : IRequest<int>
{
    /// <summary>Optional. Auto-generated when blank. Must be globally unique if supplied.</summary>
    public string?  flagCode           { get; set; }

    /// <summary>Short label for grids / dropdowns. Required.</summary>
    public string   flagName           { get; set; } = "";

    /// <summary>Full analyst-facing indicator text. Required.</summary>
    public string   flagDescription    { get; set; } = "";

    /// <summary>FK to TmX_Lookup row, Lookup_Type='FLAG_TYPE'. Required.</summary>
    public int      flagTypeLkpId      { get; set; }

    /// <summary>FK to TmX_Lookup row, Lookup_Type='FLAG_CATEGORY'. Optional.</summary>
    public int?     flagCategoryLkpId  { get; set; }

    /// <summary>FK to TmX_Lookup row, Lookup_Type='FLAG_SEVERITY'. Optional.</summary>
    public int?     severityLkpId      { get; set; }

    /// <summary>Default risk-score contribution. Defaults to 1.00 server-side if absent.</summary>
    public decimal? defaultWeight      { get; set; }

    public bool     requiresEvidence   { get; set; }
    public string?  sourceSystem       { get; set; }
}
