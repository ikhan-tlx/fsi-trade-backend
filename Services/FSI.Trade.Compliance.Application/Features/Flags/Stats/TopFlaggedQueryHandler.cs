using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Stats;

public class TopFlaggedQueryHandler
    : IRequestHandler<TopFlaggedQuery, IReadOnlyList<TopFlaggedRowDto>>
{
    private const int MaxTake = 100;

    private readonly IApplicationDbContext _db;
    public TopFlaggedQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TopFlaggedRowDto>> Handle(
        TopFlaggedQuery req, CancellationToken ct)
    {
        // Clamp Take. A consumer asking for 1,000,000 doesn't get to DoS us.
        var take = Math.Clamp(req.Take, 1, MaxTake);

        // Build the filtered transaction-flag set first; this is the
        // dominant cost. Indexed on (Flag_ID, Is_Flagged) for the
        // GROUP BY path.
        IQueryable<int> txFlagFlagIds =
            from tf in _db.TransactionFlags.AsNoTracking()
            where tf.IsFlagged
              && (req.From == null || tf.SetDate >= req.From)
              && (req.To   == null || tf.SetDate <= req.To)
            select tf.FlagId;

        // Product filter requires the txn JOIN. EF folds this into a
        // single SQL query with a sub-select; no extra round-trip.
        if (req.ProductId.HasValue)
        {
            txFlagFlagIds =
                from tf in _db.TransactionFlags.AsNoTracking()
                join t  in _db.Transactions.AsNoTracking() on tf.TransactionId equals t.TransactionId
                where tf.IsFlagged
                   && (req.From == null || tf.SetDate >= req.From)
                   && (req.To   == null || tf.SetDate <= req.To)
                   && t.ProductId == req.ProductId.Value
                select tf.FlagId;
        }

        // Aggregate: count per Flag_ID, JOIN catalogue, ORDER BY count, TAKE N.
        var query =
            from g in txFlagFlagIds.GroupBy(fid => fid)
            join c in _db.FlagCatalogues.AsNoTracking() on g.Key equals c.FlagId
            orderby g.Count() descending, c.FlagName
            select new TopFlaggedRowDto
            {
                flagId                  = c.FlagId,
                flagCode                = c.FlagCode,
                flagName                = c.FlagName,
                flagDescription         = c.FlagDescription,
                severityLkpId           = c.SeverityLkpId,
                flagCategoryLkpId       = c.FlagCategoryLkpId,
                defaultWeight           = c.DefaultWeight,
                flaggedTransactionCount = g.Count(),
                weightedScore           = c.DefaultWeight * g.Count()
            };

        return await query.Take(take).ToListAsync(ct);
    }
}
