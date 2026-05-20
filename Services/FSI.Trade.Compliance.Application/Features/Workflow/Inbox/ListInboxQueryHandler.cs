using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Inbox;

/// <summary>
/// Orchestrates the inbox feed entirely in the Application layer. JOINs
/// <c>WorkflowInbox</c> → <c>WorkflowProcessInstance</c> → <c>WorkflowProcessScheme</c>
/// directly via <c>IApplicationDbContext</c>. No vendor types touched — the
/// engine isn't called for this read.
///
/// Slice 5 LIMITATION — ordering by ProcessId because the v21 schema in this
/// deployment lacks <c>AddingDate</c> on WorkflowInbox and lacks
/// <c>LastTransitionDate</c> on WorkflowProcessInstance. Slice 6 will JOIN to
/// <c>TmX_Transaction.Created_Date</c> to recover the legacy ordering
/// behaviour (which came from <c>TmX_Application_VW.CreatedDate</c>, itself a
/// view of TmX_Transaction). At that point swap ProcessId for the transaction
/// date and the inbox shows "most recently created" at the top, matching legacy.
/// </summary>
public class ListInboxQueryHandler : IRequestHandler<ListInboxQuery, PagedResult<WorkflowInboxItem>>
{
    private readonly IApplicationDbContext   _db;
    private readonly ICurrentUserService     _current;
    private readonly WorkflowOptions         _opt;

    public ListInboxQueryHandler(
        IApplicationDbContext       db,
        ICurrentUserService         current,
        IOptions<WorkflowOptions>   opt)
    {
        _db      = db;
        _current = current;
        _opt     = opt.Value;
    }

    public async Task<PagedResult<WorkflowInboxItem>> Handle(ListInboxQuery req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Inbox requires an authenticated caller.");

        var pageSize = Math.Min(req.PageSize, _opt.InboxMaxPageSize);
        var page     = Math.Max(1, req.Page);
        var skip     = (page - 1) * pageSize;

        // WorkflowInbox.IdentityId in v21 is uniqueidentifier — must be parsed
        // from the caller's userId string. If it's not a GUID, the caller has
        // no inbox at all (their IdentityId can't match).
        if (!Guid.TryParse(userId, out var identityGuid))
            return new PagedResult<WorkflowInboxItem>
            {
                items    = new(),
                total    = 0,
                page     = page,
                pageSize = pageSize
            };

        var baseQuery =
            from inbox in _db.WorkflowInboxes.AsNoTracking()
            join proc  in _db.WorkflowProcessInstances.AsNoTracking() on inbox.ProcessId equals proc.Id
            join sch   in _db.WorkflowProcessSchemes.AsNoTracking()   on proc.SchemeId   equals sch.Id
            where inbox.IdentityId == identityGuid
            select new { inbox.ProcessId, sch.SchemeCode, proc.StateName };

        var total = await baseQuery.CountAsync(ct);

        var rows = await baseQuery
            .OrderBy(x => x.ProcessId)        // Slice 5 — stable order; Slice 6 swaps to TmX_Transaction.Created_Date
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<WorkflowInboxItem>
        {
            items    = rows.Select(r => new WorkflowInboxItem
            {
                ProcessId   = r.ProcessId,
                SchemeCode  = r.SchemeCode,
                StateName   = r.StateName,
                CreatedDate = DateTime.MinValue   // Slice 6: populate from TmX_Transaction.Created_Date join
            }).ToList(),
            total    = total,
            page     = page,
            pageSize = pageSize
        };
    }
}
