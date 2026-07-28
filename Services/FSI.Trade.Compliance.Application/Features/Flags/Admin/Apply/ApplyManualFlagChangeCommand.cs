using FSI.Trade.Compliance.Application.Features.Flags.Admin.Create;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.SetActive;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Update;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Apply;

/// <summary>
/// Maker/checker apply for a Manual Red Flag request (request-type 0006).
/// Bundles the flag record (create OR update) AND its active/inactive state into
/// ONE replayable command so the whole drawer save is approved together — a
/// checker can never approve the label while the active toggle slips through
/// un-reviewed. Reuses the existing Create / Update / SetActive handlers via
/// MediatR — no write logic duplicated.
///
/// Scope boundary: flag DEPLOYMENT (product/sub-product scope rows) is NOT part
/// of this command — scopes are placement config on an already-approved flag and
/// are edited directly in the drawer. Only the flag DEFINITION + active state
/// travel through maker/checker here.
/// </summary>
public class ApplyManualFlagChangeCommand : IRequest<int>
{
    public int?     flagId           { get; set; }   // null / 0 = create
    public string   flagName         { get; set; } = "";
    public string?  flagDescription  { get; set; }
    public int      flagTypeLkpId    { get; set; }
    public int?     severityLkpId    { get; set; }
    public bool     requiresEvidence { get; set; }
    public bool?    isActive         { get; set; }   // null = don't touch (create defaults active)
}

public class ApplyManualFlagChangeCommandHandler : IRequestHandler<ApplyManualFlagChangeCommand, int>
{
    private readonly IMediator _mediator;
    public ApplyManualFlagChangeCommandHandler(IMediator mediator) => _mediator = mediator;

    public async Task<int> Handle(ApplyManualFlagChangeCommand req, CancellationToken ct)
    {
        int id;
        if (req.flagId is { } fid && fid > 0)
        {
            await _mediator.Send(new UpdateFlagCommand
            {
                flagId           = fid,
                flagName         = req.flagName,
                flagDescription  = req.flagDescription ?? "",
                flagTypeLkpId    = req.flagTypeLkpId,
                severityLkpId    = req.severityLkpId,
                requiresEvidence = req.requiresEvidence,
            }, ct);

            // Apply the active/inactive state too (idempotent no-op if unchanged) —
            // the drawer toggles this as part of the same save.
            if (req.isActive is { } active)
                await _mediator.Send(new SetFlagActiveCommand(fid, active), ct);

            id = fid;
        }
        else
        {
            id = await _mediator.Send(new CreateFlagCommand
            {
                flagName         = req.flagName,
                flagDescription  = req.flagDescription ?? "",
                flagTypeLkpId    = req.flagTypeLkpId,
                severityLkpId    = req.severityLkpId,
                requiresEvidence = req.requiresEvidence,
            }, ct);
        }

        return id;
    }
}
