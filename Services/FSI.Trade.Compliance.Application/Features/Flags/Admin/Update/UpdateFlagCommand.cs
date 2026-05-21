using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Update;

/// <summary>
/// Slice 8 Step 6 — update a catalogue flag's editable fields.
/// Flag_Code is intentionally NOT editable — once a code is in use by
/// integrations / reports / scopes, renaming breaks every consumer.
/// To "rename" semantically, create a new flag and retire the old one
/// via <c>POST /api/v1/Flag/{id}/Deactivate</c>.
///
/// Maps to <c>PUT /api/v1/Flag/{id}</c>. Gated by <c>Flags.Manage</c>.
/// </summary>
public class UpdateFlagCommand : IRequest<int>
{
    // Path-bound — set by the controller from {id}; not part of the body.
    public int      flagId             { get; set; }

    public string   flagName           { get; set; } = "";
    public string   flagDescription    { get; set; } = "";
    public int      flagTypeLkpId      { get; set; }
    public int?     flagCategoryLkpId  { get; set; }
    public int?     severityLkpId      { get; set; }
    public decimal? defaultWeight      { get; set; }
    public bool     requiresEvidence   { get; set; }
    public string?  sourceSystem       { get; set; }
}
