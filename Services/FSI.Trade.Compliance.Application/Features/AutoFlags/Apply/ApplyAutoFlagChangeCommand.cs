using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.AutoFlags.Apply;

/// <summary>
/// Maker/checker APPLY command for Auto Flags — the Auto Flag analogue of
/// <c>ApplyRoleChangeCommand</c> / <c>ApplyManualFlagChangeCommand</c>.
///
/// The maker's proposed config (core + scope + rules — the full
/// <see cref="AutoFlagWriteModel"/>) is parked verbatim in <c>TmX_Request.Entity_Json</c>
/// on submit and REPLAYED here only when a checker approves. It creates
/// (<see cref="flagId"/> null) or updates (<see cref="flagId"/> set) the flag from the
/// proposed config and then brings it to its approved, live state
/// (<c>Status = Active</c>, <c>ActiveFlag = true</c>).
///
/// The point of routing an edit-of-an-Active-flag through this command is that the
/// LIVE flag row is never touched while the request is pending, so a REJECT is a pure
/// no-op — the currently-approved flag keeps evaluating unchanged. (A brand-new /
/// Draft flag keeps its build-and-dry-run flow and is activated via
/// <c>ApproveAutoFlagCommand</c> instead.)
/// </summary>
public class ApplyAutoFlagChangeCommand : AutoFlagWriteModel, IRequest<int>
{
    /// <summary>Existing flag to update; null/0 = create a new flag.</summary>
    public int? flagId { get; set; }
}

public class ApplyAutoFlagChangeCommandHandler : IRequestHandler<ApplyAutoFlagChangeCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IMediator             _mediator;

    public ApplyAutoFlagChangeCommandHandler(
        IApplicationDbContext db, ICurrentUserService current, IMediator mediator)
    { _db = db; _current = current; _mediator = mediator; }

    public async Task<int> Handle(ApplyAutoFlagChangeCommand req, CancellationToken ct)
    {
        var isUpdate = req.flagId is int fid && fid > 0;
        int flagId;

        // Reuse the real Create/Update write handlers (all their validation + the shared
        // scope/rule writers) — no duplicated persistence logic.
        if (isUpdate)
        {
            await _mediator.Send(new UpdateAutoFlagCommand
            {
                FlagId          = req.flagId!.Value,
                flagCode        = req.flagCode,
                name            = req.name,
                description     = req.description,
                severity        = req.severity,
                scopeProductIds = req.scopeProductIds,
                scopes          = req.scopes,
                raisePerMatch   = req.raisePerMatch,
                rules           = req.rules,
            }, ct);
            flagId = req.flagId!.Value;
        }
        else
        {
            flagId = await _mediator.Send(new CreateAutoFlagCommand
            {
                flagCode        = req.flagCode,
                name            = req.name,
                description     = req.description,
                severity        = req.severity,
                scopeProductIds = req.scopeProductIds,
                scopes          = req.scopes,
                raisePerMatch   = req.raisePerMatch,
                rules           = req.rules,
            }, ct);
        }

        // Approving the request activates the flag (it starts evaluating). Mirrors
        // ApproveAutoFlagCommandHandler so both approval routes converge on the same
        // live state.
        var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == flagId && !f.IsDeleted, ct);
        if (flag is not null)
        {
            var u   = _current.UserName ?? _current.UserId ?? "system";
            var now = DateTime.UtcNow;
            flag.ActiveFlag      = true;
            flag.Status          = "Active";
            flag.ApprovedBy      = u;
            flag.ApprovedDate    = now;
            // Bump the config version on an approved edit so the change is traceable.
            flag.VersionNo       = (flag.VersionNo ?? 1) + (isUpdate ? 1 : 0);
            flag.LastUpdatedBy   = u;
            flag.LastUpdatedDate = now;
            await _db.SaveChangesAsync(ct);
        }

        return flagId;
    }
}
