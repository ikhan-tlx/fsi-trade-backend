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

        // 3b. Structured KPI / segment filters (Option A redesign). Additive,
        //     AND-combined on top of scope + freetext. Omitted filters are
        //     no-ops, so the classic list call is unaffected.
        if (req.ProductId is int productId)
            q = q.Where(t => t.ProductId == productId);

        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            var s = req.Status.Trim();
            q = q.Where(t => t.Status != null && EF.Functions.Like(t.Status, $"%{s}%"));
        }

        if (req.Mine == true)
            q = q.Where(t => t.InboxUserId == userId);

        if (req.CreatedToday == true)
        {
            var todayStart    = DateTime.UtcNow.Date;
            var tomorrowStart = todayStart.AddDays(1);
            q = q.Where(t => t.CreatedDate >= todayStart && t.CreatedDate < tomorrowStart);
        }

        if (req.Flagged == true)
            q = q.Where(t => _db.TransactionFlags.Any(f => f.TransactionId == t.TransactionId && f.IsFlagged));

        // 4. Sort. Default: most recent Created_Date first (matches FE
        //    "transactionId-desc" only loosely — Created_Date is the user-
        //    meaningful chronology).
        q = ApplySort(q, req.Sort);

        // 5. Page + project. Projection keeps the wire payload tight —
        //    20 columns instead of the view's 30.
        var result = await q.ToPagedResultAsync(req,
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

        // 6. Enrich the page (Option A redesign). Two batched lookups keyed
        //    on JUST the current page's transaction ids — never the whole
        //    scoped set — so the cost is bounded by PageSize. Neither field
        //    exists on TmX_Transaction_VW, hence the secondary round-trips.
        await EnrichPageAsync(result.items, ct);

        return result;
    }

    /// <summary>
    /// Fills <see cref="TransactionListItemDto.subProductName"/> and
    /// <see cref="TransactionListItemDto.flagCount"/> for the rows on the
    /// current page. Both are additive; absent data leaves the chip blank /
    /// the count at 0. Sub-product = TmX_Transaction.Sub_Product_Tab_Id →
    /// TmX_Tab.Tab_Name (NULL for KYC / single-tab / legacy rows). Flag count
    /// = number of TmX_Transaction_Flag rows currently set (Is_Flagged = 1).
    /// </summary>
    private async Task EnrichPageAsync(List<TransactionListItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var ids = items.Select(i => i.transactionId).ToList();

        // Sub-product tab name (one query, joined to the tab master).
        var subProducts = await (
                from t in _db.Transactions.AsNoTracking()
                where ids.Contains(t.TransactionId) && t.SubProductTabId != null
                join tab in _db.Tabs.AsNoTracking() on t.SubProductTabId.Value equals tab.TabId
                select new { t.TransactionId, tab.TabName })
            .ToListAsync(ct);

        var subMap = subProducts
            .GroupBy(x => x.TransactionId)
            .ToDictionary(g => g.Key, g => g.First().TabName);

        // Flagged-count per transaction (only currently-raised flags).
        var flagCounts = await _db.TransactionFlags.AsNoTracking()
            .Where(f => ids.Contains(f.TransactionId) && f.IsFlagged)
            .GroupBy(f => f.TransactionId)
            .Select(g => new { TransactionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var flagMap = flagCounts.ToDictionary(x => x.TransactionId, x => x.Count);

        // Row severity = highest severity among the row's currently-raised
        // flags. Join active flags → flag catalogue's Severity_Lkp_Id →
        // FLAG_SEVERITY lookup. One batched query for the page's ids.
        var sevRows = await (
                from f in _db.TransactionFlags.AsNoTracking()
                where ids.Contains(f.TransactionId) && f.IsFlagged
                join c in _db.FlagCatalogues.AsNoTracking() on f.FlagId equals c.FlagId
                where c.SeverityLkpId != null
                join l in _db.Lookups.AsNoTracking() on c.SeverityLkpId.Value equals l.Id
                where l.LookupType == "FLAG_SEVERITY"
                select new { f.TransactionId, l.VisibleValue, l.HiddenValue })
            .ToListAsync(ct);

        // Pick the max-rank severity per transaction.
        var sevMap = sevRows
            .GroupBy(x => x.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(x => new { Name = x.VisibleValue, Rank = SeverityRank(x.HiddenValue) })
                    .OrderByDescending(x => x.Rank)
                    .First());

        foreach (var item in items)
        {
            if (subMap.TryGetValue(item.transactionId, out var name))
                item.subProductName = name;
            item.flagCount = flagMap.TryGetValue(item.transactionId, out var c) ? c : 0;
            if (sevMap.TryGetValue(item.transactionId, out var sev))
            {
                item.severity     = sev.Name;
                item.severityRank = sev.Rank;
            }
        }
    }

    /// <summary>
    /// Ranks a FLAG_SEVERITY hidden value so we can pick the worst flag on a
    /// transaction. Unknown values rank 0 (treated as informational).
    /// </summary>
    private static int SeverityRank(string? hidden) => (hidden ?? "").Trim().ToUpperInvariant() switch
    {
        "CRITICAL" => 4,
        "HIGH"     => 3,
        "MEDIUM"   => 2,
        "LOW"      => 1,
        _          => 0,
    };

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
