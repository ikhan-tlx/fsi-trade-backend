using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Extensions;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Locations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.List;

/// <summary>
/// Resolves the caller's "scope" — locations they oversee plus branches
/// they're assigned to — then queries <c>TmX_Transaction_VW</c> with the
/// legacy predicate:
///
/// <code>
///   creatorLocationId IN @locationIds OR
///   companyBranchId   IN @branchIds   OR
///   creatorId         == @userId
/// </code>
///
/// Then applies <see cref="PagedQuery.Filter"/> as a freetext OR-search
/// across transaction number / customer name / national identifier
/// (cheap, indexed-ish in the view), applies sort, and pages.
/// </summary>
public class ListTransactionsQueryHandler
    : IRequestHandler<ListTransactionsQuery, PagedResult<TransactionListItemDto>>
{
    private readonly IApplicationDbContext        _db;
    private readonly ICurrentUserService          _current;
    private readonly ILocationHierarchyService    _locations;

    public ListTransactionsQueryHandler(
        IApplicationDbContext     db,
        ICurrentUserService       current,
        ILocationHierarchyService locations)
    {
        _db        = db;
        _current   = current;
        _locations = locations;
    }

    public async Task<PagedResult<TransactionListItemDto>> Handle(ListTransactionsQuery req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Transaction list requires an authenticated caller.");

        // 1. Resolve caller's scope: location-tree if they have a Location_ID,
        //    otherwise effective branch assignments. Legacy fallback rule
        //    intact — Location wins when present, branches only when not.
        var caller = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.LocationId })
            .FirstOrDefaultAsync(ct);

        IReadOnlyCollection<int> locationIds = Array.Empty<int>();
        IReadOnlyCollection<int> branchIds   = Array.Empty<int>();

        if (caller is not null && caller.LocationId.HasValue)
        {
            locationIds = await _locations.GetSelfAndDescendantIdsAsync(caller.LocationId.Value, ct);
        }
        else
        {
            var now = DateTime.UtcNow;
            branchIds = await _db.CompanyBranchUserMappings.AsNoTracking()
                .Where(m =>
                       m.UserId             == userId &&
                       m.ActiveFlag                  &&
                       m.EffectiveStartDate <= now    &&
                       m.EffectiveEndDate   >= now)
                .Select(m => m.CompanyBranchId)
                .Distinct()
                .ToListAsync(ct);
        }

        // 2. Base predicate against the view. The view exposes both
        //    Creator_Location_Id and Company_Branch_Id, plus Creator_Id —
        //    same triple-OR as the legacy.
        IQueryable<TransactionListView> q =
            _db.TransactionList.AsNoTracking();

        q = q.Where(t =>
                (t.CreatorLocationId.HasValue && locationIds.Contains(t.CreatorLocationId.Value)) ||
                (t.CompanyBranchId.HasValue   && branchIds.Contains(t.CompanyBranchId.Value)) ||
                 t.CreatorId == userId);

        // 3. Freetext filter — single search box across the columns the
        //    FE's grid surfaces. Kept simple; structured column-level
        //    filters land in Slice 7 if needed.
        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(t =>
                   (t.TransactionNumber       != null && EF.Functions.Like(t.TransactionNumber,       $"%{f}%")) ||
                   (t.CustomerName            != null && EF.Functions.Like(t.CustomerName,            $"%{f}%")) ||
                   (t.NationalIdentifierValue != null && EF.Functions.Like(t.NationalIdentifierValue, $"%{f}%")) ||
                   (t.CustomerCode            != null && EF.Functions.Like(t.CustomerCode,            $"%{f}%")) ||
                   (t.ProductName             != null && EF.Functions.Like(t.ProductName,             $"%{f}%")));
        }

        // 4. Sort. Default: most recent Created_Date first (matches FE
        //    "transactionId-desc" only loosely — Created_Date is the user-
        //    meaningful chronology).
        q = ApplySort(q, req.Sort);

        // 5. Page + project. Projection keeps the wire payload tight —
        //    20 columns instead of the view's 30.
        return await q.ToPagedResultAsync(req,
            source => source.Select(t => new TransactionListItemDto
            {
                transactionId           = t.TransactionId,
                transactionNumber       = t.TransactionNumber,
                transactionType         = t.TransactionType,
                transactionDate         = t.TransactionDate,
                createdDate             = t.CreatedDate,
                productId               = t.ProductId,
                productName             = t.ProductName,
                customerCode            = t.CustomerCode,
                customerName            = t.CustomerName,
                nationalIdentifierValue = t.NationalIdentifierValue,
                branchName              = t.BranchName,
                creatorName             = t.CreatorName,
                currentState            = t.CurrentState,
                inboxUserId             = t.InboxUserId,
                inboxName               = t.InboxName,
                status                  = t.Status,
                processInstanceId       = t.ProcessInstanceId,
                transactionStatusLkp    = t.TransactionStatusLkp
            }),
            ct);
    }

    private static IQueryable<TransactionListView> ApplySort(
        IQueryable<TransactionListView> q, string? sort)
    {
        var tokens = PagedQueryExtensions.ParseSort(sort).ToList();
        if (tokens.Count == 0)
            return q.OrderByDescending(t => t.CreatedDate);

        // Honour only the first sort token for now — most grids only sort by
        // one column at a time. Multi-column sort is a Slice 7 concern.
        var (field, desc) = tokens[0];
        return field switch
        {
            "transactionid"       => desc ? q.OrderByDescending(t => t.TransactionId)        : q.OrderBy(t => t.TransactionId),
            "transactionnumber"   => desc ? q.OrderByDescending(t => t.TransactionNumber)    : q.OrderBy(t => t.TransactionNumber),
            "transactiondate"     => desc ? q.OrderByDescending(t => t.TransactionDate)      : q.OrderBy(t => t.TransactionDate),
            "createddate"         => desc ? q.OrderByDescending(t => t.CreatedDate)          : q.OrderBy(t => t.CreatedDate),
            "customername"        => desc ? q.OrderByDescending(t => t.CustomerName)         : q.OrderBy(t => t.CustomerName),
            "productname"         => desc ? q.OrderByDescending(t => t.ProductName)          : q.OrderBy(t => t.ProductName),
            "branchname"          => desc ? q.OrderByDescending(t => t.BranchName)           : q.OrderBy(t => t.BranchName),
            "currentstate"        => desc ? q.OrderByDescending(t => t.CurrentState)         : q.OrderBy(t => t.CurrentState),
            "status"              => desc ? q.OrderByDescending(t => t.Status)               : q.OrderBy(t => t.Status),
            _                     => q.OrderByDescending(t => t.CreatedDate)
        };
    }
}
