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
/// Slice 13 — full rewrite. The previous Slice 5 implementation joined
/// only WorkflowInbox → ProcessInstance → Scheme and emitted 4 fields,
/// which left every column of the FE Inbox grid empty (Customer,
/// Product, Branch, Creator, App. Date — all blank).
///
/// <para>
/// This handler reads from the same entities <c>TmX_Application_VW</c>
/// reads from in legacy (the Transaction branch), in pure LINQ, with
/// no dependency on the view. Returns the listing-shape DTO the FE
/// grid actually renders; for the full transaction payload the FE
/// drills into <c>GET /Transaction/{id}</c> (Slice 6 + 10).
/// </para>
///
/// Query shape mirrors the legacy view's Transaction branch:
/// <code>
///   inbox  (filter IdentityId == caller)
///     → ProcessInstance     (via inbox.ProcessId == proc.Id)
///     → Transaction         (via proc.RootProcessId == tx.ProcessInstanceId)
///     → Product             (inner)
///     → Status Lookup       (left)
///     → CompanyBranch       (left)
///     → CustomerSnapshot    (left, MAX by CreatedDate per Transaction)
///     → Creator User        (left, via tx.UserId)
///     → MAX TransitionTime  (subquery, excluding 'SetState')
/// </code>
///
/// AssignedTo* fields are populated from the caller's TmX_User row
/// (resolved once outside the join) — for the user-filtered inbox the
/// inbox actor is always the caller. Field shape stays generic so an
/// admin "view someone else's inbox" overload can populate it
/// differently later without breaking the FE contract.
///
/// Dedup: GROUP BY Transaction_Id taking the row with the latest
/// transition time. Handles the rare case where a user lands in
/// WorkflowInbox twice for the same process (e.g. two role assignments).
/// </summary>
public class ListInboxQueryHandler : IRequestHandler<ListInboxQuery, PagedResult<WorkflowInboxItem>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly WorkflowOptions       _opt;

    public ListInboxQueryHandler(
        IApplicationDbContext     db,
        ICurrentUserService       current,
        IOptions<WorkflowOptions> opt)
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

        // WorkflowInbox.IdentityId is uniqueidentifier in this deployment.
        // If the caller's userId isn't a Guid, their inbox is empty by
        // definition (no IdentityId can match).
        if (!Guid.TryParse(userId, out var identityGuid))
            return new PagedResult<WorkflowInboxItem>
            {
                items    = new(),
                total    = 0,
                page     = page,
                pageSize = pageSize
            };

        // Resolve the caller's display attributes once. assignedTo* in
        // every grid row points back at the caller (user-filtered inbox);
        // projecting from a single lookup keeps the JOIN graph smaller.
        var caller = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync(ct);

        var callerName  = ComposeDisplayName(caller?.FirstName, caller?.LastName);
        var callerEmail = caller?.Email;

        // ---------- Main query ----------
        // Mirrors the Transaction branch of TmX_Application_VW. Left
        // outer joins where the source can legitimately be null
        // (Branch, Status lookup, Creator user). Inner joins where
        // it's a hard FK that must be present (Process Instance,
        // Transaction itself, Product).
        //
        // EF Core 8 can't translate GroupBy(...).Select(g => g.OrderBy(...).First())
        // so the dedup happens in memory after ToListAsync. The
        // user-filtered inbox is naturally small (one user's rows),
        // and the dedup case is rare (user in inbox twice for the
        // same transaction via two role-based fills), so the
        // memory cost is negligible.
        var baseQuery =
            from inbox in _db.WorkflowInboxes.AsNoTracking()
            where inbox.IdentityId == identityGuid

            join proc in _db.WorkflowProcessInstances.AsNoTracking()
                on inbox.ProcessId equals proc.Id

            // Transaction.ProcessInstanceId equals wfInstance.RootProcessId,
            // not Id — the legacy view uses RootProcessId so sub-processes
            // resolve back to their owning transaction.
            join tx in _db.Transactions.AsNoTracking()
                on proc.RootProcessId equals tx.ProcessInstanceId

            join prod in _db.Products.AsNoTracking()
                on tx.ProductId equals prod.ProductId

            join statusLkp in _db.Lookups.AsNoTracking()
                on tx.TransactionStatusLkp equals statusLkp.Id into statusJ
            from statusLkp in statusJ.DefaultIfEmpty()

            join branch in _db.CompanyBranches.AsNoTracking()
                on tx.CompanyBranchId equals branch.CompanyBranchId into branchJ
            from branch in branchJ.DefaultIfEmpty()

            join creator in _db.Users.AsNoTracking()
                on tx.UserId equals creator.Id into creatorJ
            from creator in creatorJ.DefaultIfEmpty()

            select new
            {
                ProcId        = proc.Id,
                tx.TransactionId,
                tx.ProcessInstanceId,
                tx.TransactionNumber,
                tx.TransactionStatusLkp,
                tx.CompanyBranchId,
                tx.ProductId,
                tx.TransactionDate,
                tx.CreatedDate,
                tx.UserId,

                Status        = statusLkp != null ? statusLkp.VisibleValue : null,
                ProductName   = prod.ProductName,
                BranchName    = branch != null ? branch.BranchName  : null,
                CreatorFirst  = creator != null ? creator.FirstName : null,
                CreatorLast   = creator != null ? creator.LastName  : null,
                CreatorEmail  = creator != null ? creator.Email     : null
            };

        // Pull raw rows. One row per (caller's inbox entry × matching tx).
        // For a user-filtered inbox this is typically 1-200 rows.
        var raw = await baseQuery.ToListAsync(ct);

        if (raw.Count == 0)
            return new PagedResult<WorkflowInboxItem>
            {
                items    = new(),
                total    = 0,
                page     = page,
                pageSize = pageSize
            };

        // Batched enrichment — TransitionTime per process + CustomerName
        // per transaction. Pulled separately so the main query stays
        // EF-translatable.
        var processIds     = raw.Select(r => r.ProcId).Distinct().ToList();
        var transactionIds = raw.Select(r => r.TransactionId).Distinct().ToList();

        var transitionByProcess = await _db.WorkflowProcessTransitionHistories.AsNoTracking()
            .Where(h => processIds.Contains(h.ProcessId) && h.TriggerName != "SetState")
            .GroupBy(h => h.ProcessId)
            .Select(g => new { ProcessId = g.Key, MaxTime = g.Max(h => (DateTime?)h.TransitionTime) })
            .ToListAsync(ct);
        var transitionByProcessDict = transitionByProcess
            .ToDictionary(x => x.ProcessId, x => x.MaxTime);

        // Most-recent customer snapshot per transaction (highest CustomerMasterId
        // serves as the recency proxy — snapshots are insert-only).
        var customerRows = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.TransactionId.HasValue && transactionIds.Contains(c.TransactionId.Value))
            .Select(c => new { c.TransactionId, c.CustomerMasterId, c.CustomerName })
            .ToListAsync(ct);
        var customerNameByTxn = customerRows
            .GroupBy(c => c.TransactionId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.CustomerMasterId).First().CustomerName);

        // Project + in-memory dedup + sort + page.
        var enriched = raw
            .Select(r => new
            {
                r.TransactionId,
                r.ProcessInstanceId,
                r.TransactionNumber,
                r.TransactionStatusLkp,
                r.CompanyBranchId,
                r.ProductId,
                r.TransactionDate,
                r.CreatedDate,
                r.UserId,
                r.Status,
                r.ProductName,
                r.BranchName,
                r.CreatorFirst,
                r.CreatorLast,
                r.CreatorEmail,
                TransitionTime = transitionByProcessDict.TryGetValue(r.ProcId, out var t) ? t : null,
                CustomerName   = customerNameByTxn.TryGetValue(r.TransactionId, out var c) ? c : null
            })
            .GroupBy(r => r.TransactionId)
            .Select(g => g.OrderByDescending(x => x.TransitionTime).First())
            .OrderByDescending(r => r.TransitionTime)
            .ThenByDescending(r => r.CreatedDate)
            .ToList();

        var total = enriched.Count;
        var rows  = enriched.Skip(skip).Take(pageSize).ToList();

        var items = rows.Select(r => new WorkflowInboxItem
        {
            transactionId        = r.TransactionId,
            processInstanceId    = r.ProcessInstanceId,
            transactionNumber    = r.TransactionNumber,

            transactionStatusLkp = r.TransactionStatusLkp,
            status               = r.Status,

            customerName         = r.CustomerName,
            productName          = r.ProductName,
            branchName           = r.BranchName,

            creatorName          = ComposeDisplayName(r.CreatorFirst, r.CreatorLast),
            creatorEmail         = r.CreatorEmail,

            // User-filtered inbox → assignedTo always the caller.
            // Same shape stays compatible with a future admin "view
            // any user's inbox" overload that resolves per-row.
            assignedToId         = caller?.Id,
            assignedToName       = callerName,
            assignedToEmail      = callerEmail,

            transactionDate      = r.TransactionDate,
            createdDate          = r.CreatedDate,
            transitionTime       = r.TransitionTime
        }).ToList();

        return new PagedResult<WorkflowInboxItem>
        {
            items    = items,
            total    = total,
            page     = page,
            pageSize = pageSize
        };
    }

    /// <summary>"First Last", trimmed. Empty string if both parts are blank.</summary>
    private static string ComposeDisplayName(string? first, string? last)
    {
        var f = (first ?? "").Trim();
        var l = (last  ?? "").Trim();
        if (f.Length == 0 && l.Length == 0) return "";
        return (f + " " + l).Trim();
    }
}
