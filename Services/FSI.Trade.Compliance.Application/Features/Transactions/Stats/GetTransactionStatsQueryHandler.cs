using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Locations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Stats;

/// <summary>
/// Computes the Trade Repository KPI strip. Resolves the caller's scope with
/// the SAME predicate as <see cref="List.ListTransactionsQueryHandler"/>
/// (location-tree OR effective branches OR own-created) so every count
/// reconciles with the grid, then runs a handful of cheap COUNT/GROUP BY
/// queries against <c>TmX_Transaction_VW</c>. No rows are materialised.
/// </summary>
public class GetTransactionStatsQueryHandler
    : IRequestHandler<GetTransactionStatsQuery, TransactionStatsDto>
{
    private readonly IApplicationDbContext     _db;
    private readonly ICurrentUserService       _current;
    private readonly ILocationHierarchyService _locations;
    // Single SLA window (days) — the SAME setting that drives the SLA-breach
    // notification scanner, so the grid aging badge and the notifications never
    // diverge (was a separate TradeRepository.SlaDays).
    private readonly SlaOptions               _sla;

    public GetTransactionStatsQueryHandler(
        IApplicationDbContext             db,
        ICurrentUserService              current,
        ILocationHierarchyService        locations,
        IOptionsSnapshot<SlaOptions>     sla)
    {
        _db        = db;
        _current   = current;
        _locations = locations;
        _sla       = sla.Value;
    }

    public async Task<TransactionStatsDto> Handle(GetTransactionStatsQuery req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Transaction stats require an authenticated caller.");

        // 1. Resolve caller scope — identical rule to the list handler.
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

        // 2. Same scoped base query as the grid.
        IQueryable<TransactionListView> q = _db.TransactionList.AsNoTracking()
            .Where(t =>
                (t.CreatorLocationId.HasValue && locationIds.Contains(t.CreatorLocationId.Value)) ||
                (t.CompanyBranchId.HasValue   && branchIds.Contains(t.CompanyBranchId.Value)) ||
                 t.CreatorId == userId);

        // 3. KPI counts. Each is a single COUNT/GROUP BY — no row fetch.
        var todayStart    = DateTime.UtcNow.Date;
        var tomorrowStart = todayStart.AddDays(1);

        var total = await q.CountAsync(ct);

        var awaitingMyAction = await q
            .Where(t => t.InboxUserId == userId)
            .CountAsync(ct);

        var sentToAuthorizer = await q
            .Where(t => t.Status != null && EF.Functions.Like(t.Status, "%Authorizer%"))
            .CountAsync(ct);

        var createdToday = await q
            .Where(t => t.CreatedDate >= todayStart && t.CreatedDate < tomorrowStart)
            .CountAsync(ct);

        // Flagged = at least one currently-raised flag (correlated EXISTS).
        var flagged = await q
            .Where(t => _db.TransactionFlags.Any(f => f.TransactionId == t.TransactionId && f.IsFlagged))
            .CountAsync(ct);

        var byProduct = await q
            .GroupBy(t => new { t.ProductId, t.ProductName })
            .Select(g => new ProductStatDto
            {
                productId   = g.Key.ProductId,
                productName = g.Key.ProductName,
                count       = g.Count()
            })
            .OrderByDescending(p => p.count)
            .ToListAsync(ct);

        return new TransactionStatsDto
        {
            total            = total,
            awaitingMyAction = awaitingMyAction,
            sentToAuthorizer = sentToAuthorizer,
            createdToday     = createdToday,
            flagged          = flagged,
            slaDays          = _sla.WindowDays,
            byProduct        = byProduct
        };
    }
}
