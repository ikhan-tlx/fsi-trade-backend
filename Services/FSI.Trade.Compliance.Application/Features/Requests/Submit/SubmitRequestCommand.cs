using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.LookupAdmin;
using FSI.Trade.Compliance.Application.Features.Requests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Requests.Submit;

/// <summary>
/// Checker decision on a maker/checker request (the Requests detail page's
/// Submit button). Slice 14. Records the chosen RequestStatusCode (approve /
/// reject / etc.) and stamps the executing checker on the row
/// (Last_Updated_By / Date).
///
/// Scope note: this transitions the REQUEST's status only. Applying the
/// approved Entity_Json to the underlying entity (auto-creating/updating the
/// user/role/branch on approval) is a separate workflow step not yet wired —
/// the FE renders the entity read-only and only sends the status decision.
/// The command accepts the full FE body but acts on RequestId + the new status.
/// </summary>
public class SubmitRequestCommand : IRequest<long>
{
    public long     RequestId         { get; set; }
    public string?  RequestTypeCode   { get; set; }
    public string?  EntityStatusCode  { get; set; }
    public string?  RequestStatusCode { get; set; }   // the checker's decision
    public string?  EntityId          { get; set; }
    public string?  RejectReason      { get; set; }   // required when rejecting

    /// <summary>Server-set only (controller overwrites any body value). True if the
    /// caller may decide their OWN request — Requests.ApproveOwn / bootstrap admin.</summary>
    public bool     CanApproveOwn     { get; set; }
}

public class SubmitRequestCommandHandler : IRequestHandler<SubmitRequestCommand, long>
{
    private readonly IApplicationDbContext   _db;
    private readonly ICurrentUserService     _current;
    private readonly IMediator               _mediator;
    private readonly IApprovalReplayRegistry _registry;

    public SubmitRequestCommandHandler(
        IApplicationDbContext db, ICurrentUserService current,
        IMediator mediator, IApprovalReplayRegistry registry)
    {
        _db       = db;
        _current  = current;
        _mediator = mediator;
        _registry = registry;
    }

    public async Task<long> Handle(SubmitRequestCommand req, CancellationToken ct)
    {
        if (req.RequestId <= 0)
            throw new ConflictException("request_id_required", "A valid RequestId is required.");
        if (string.IsNullOrWhiteSpace(req.RequestStatusCode))
            throw new ConflictException("request_status_required", "A RequestStatusCode (decision) is required.");

        var entity = await _db.Requests.FirstOrDefaultAsync(x => x.RequestId == req.RequestId, ct)
                     ?? throw new NotFoundException("request_not_found", $"Request '{req.RequestId}' not found.");

        var decision = req.RequestStatusCode!.Trim();
        const string RejectCode  = "0013";
        const string ApproveCode = "0012";
        var checker = _current.UserName ?? _current.UserId ?? "unknown";

        // Guard double-decisions — only a still-pending request can be acted on.
        if (entity.RequestStatusCode == ApproveCode || entity.RequestStatusCode == RejectCode)
            throw new ConflictException("request_already_decided", "This request has already been decided.");

        // Segregation of duties — a maker can't decide their own request unless they
        // hold Requests.ApproveOwn (admin). Applies to approve AND reject.
        var isOwn = string.Equals(entity.CreatedBy, checker, StringComparison.OrdinalIgnoreCase);
        if (isOwn && !req.CanApproveOwn)
            throw new ConflictException("cannot_approve_own_request",
                "You cannot approve or reject your own request — another checker must act on it.");

        if (decision == RejectCode && string.IsNullOrWhiteSpace(req.RejectReason))
            throw new ConflictException("reject_reason_required", "A reason is required to reject a request.");

        // Apply-on-approval. Runs BEFORE the status flip so a failing (re-run)
        // validation leaves the request pending and surfaces the error to the checker.
        if (decision == ApproveCode)
        {
            if (entity.RequestTypeCode == LookupRequestCodes.RequestType_Lookup)
            {
                // Lookup Modification Requests keep their bespoke apply command.
                await _mediator.Send(new ApplyLookupChangeCommand(entity.EntityJson), ct);
            }
            else if (!string.IsNullOrWhiteSpace(entity.CommandKey))
            {
                // Command-shaped rows (created by the generic Submit endpoint): replay
                // the maker's original command through its real write handler — all its
                // validation + persistence, no duplicated apply logic. Legacy rows have
                // a null Command_Key and fall through to a status-only decision.
                var command = _registry.BuildReplayCommand(
                    entity.RequestTypeCode, entity.EntityStatusCode, entity.EntityId, entity.EntityJson);
                await _mediator.Send(command, ct);
            }
        }
        else if (decision == RejectCode && entity.RequestTypeCode == "0005"
                 && entity.EntityStatusCode == "0001")
        {
            // Auto Flag ACTIVATE-NEW (action 0001) rejected → return the Draft/new flag to
            // Draft so its author can revise and resubmit (it sat in PendingApproval while
            // the request was open).
            //
            // An EDIT of an already-Active flag (action 0002) is parked as a replay and
            // never touches the live row, so its rejection is a pure no-op — the live flag
            // keeps evaluating with its currently-approved config. (Hence this branch is
            // gated to action 0001 only.)
            if (int.TryParse(entity.EntityId, out var flagId))
            {
                var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == flagId && !f.IsDeleted, ct);
                if (flag is not null)
                {
                    flag.Status          = "Draft";
                    flag.LastUpdatedBy   = checker;
                    flag.LastUpdatedDate = DateTime.UtcNow;
                }
            }
        }

        entity.RequestStatusCode = decision;
        entity.RejectReason      = decision == RejectCode ? req.RejectReason!.Trim() : null;
        entity.LastUpdatedBy     = _current.UserName ?? _current.UserId ?? "unknown";
        entity.LastUpdatedDate   = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return entity.RequestId;
    }
}
