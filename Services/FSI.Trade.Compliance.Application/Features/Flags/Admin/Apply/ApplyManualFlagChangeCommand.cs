using FSI.Trade.Compliance.Application.Features.Flags.Admin.Create;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.SetActive;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Update;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Apply;

/// <summary>One product/sub-product placement for a new flag, parked on the create request.</summary>
public class ManualFlagScopeInput
{
    public int? productId { get; set; }   // null = ALL products (global)
    public int? tabId     { get; set; }   // null = product-level / all-products
}

/// <summary>
/// Maker/checker apply for a Manual Red Flag request (request-type 0006).
/// Bundles the flag record (create OR update) AND its active/inactive state into
/// ONE replayable command so the whole drawer save is approved together — a
/// checker can never approve the label while the active toggle slips through
/// un-reviewed. Reuses the existing Create / Update / SetActive handlers via
/// MediatR — no write logic duplicated.
///
/// Scope on CREATE: the maker can pick product/sub-product placements in the same
/// drawer; they ride along in <see cref="scopes"/> and are created here right after
/// the flag so a new flag can be approved already-scoped (parity with the edit
/// drawer). On UPDATE, scope placement stays an immediate edit on the approved flag
/// (there is a live id to attach to), so <see cref="scopes"/> is ignored for updates.
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
    public List<ManualFlagScopeInput>? scopes { get; set; }  // create-only placement
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

            // Create-time scope placements (if any) — attach them to the brand-new
            // flag so it is approved already-scoped. De-dupe defensively; the add
            // handler also rejects exact duplicates.
            if (req.scopes is { Count: > 0 })
            {
                var seen = new HashSet<(int?, int?)>();
                foreach (var s in req.scopes)
                {
                    if (!seen.Add((s.productId, s.tabId))) continue;
                    await _mediator.Send(new AddFlagScopeCommand
                    {
                        flagId     = id,
                        productId  = s.productId,
                        tabId      = s.tabId,
                        activeFlag = true,
                    }, ct);
                }
            }
        }

        return id;
    }
}
