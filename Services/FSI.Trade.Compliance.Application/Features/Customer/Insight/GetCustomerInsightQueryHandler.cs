using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Customer.List;       // ExtractRiskScore / BandLabel
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;   // RiskBandClassifier
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Customer.Insight;

/// <summary>
/// Aggregates one customer's transactions into the chart series the detail
/// view renders. Resolves Customer_Code → Transaction_Id via the customer
/// snapshots, loads those transactions, then groups by month / product /
/// status. Product + status labels resolve from Products / APPLICATION_STATUS.
/// </summary>
public class GetCustomerInsightQueryHandler
    : IRequestHandler<GetCustomerInsightQuery, CustomerInsightDto>
{
    private const int PlatformDefaultTenantId = 1;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public GetCustomerInsightQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db = db;
        _current = current;
    }

    public async Task<CustomerInsightDto> Handle(GetCustomerInsightQuery req, CancellationToken ct)
    {
        var tenantId = _current.TenantId ?? PlatformDefaultTenantId;
        var code = (req.CustomerId ?? "").Trim();

        var dto = new CustomerInsightDto { customerCode = code };
        if (code.Length == 0) return dto;

        var snaps = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CustomerCode == code)
            .Select(c => new { c.CustomerName, c.CreatedDate, c.LastUpdatedDate, c.UdfData, c.TransactionId })
            .ToListAsync(ct);

        if (snaps.Count == 0) return dto;

        var latest = snaps.OrderByDescending(x => x.CreatedDate).First();
        dto.customerName     = latest.CustomerName;
        dto.riskScore        = ListCustomersQueryHandler.ExtractRiskScore(latest.UdfData);
        dto.riskBand         = ListCustomersQueryHandler.BandLabel(RiskBandClassifier.Classify(dto.riskScore));
        dto.onboardedDate    = snaps.Min(x => x.CreatedDate);
        dto.lastActivityDate = snaps.Max(x => x.LastUpdatedDate ?? x.CreatedDate);

        // Periodic Customer Review — schedule-backed next-review-date + risk band (same source
        // the transaction Customer tab reads, so both views stay consistent).
        var sched = await _db.CustomerReviewSchedules.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.CustomerCode == code)
            .Select(s => new { s.NextReviewDate, s.CurrentRiskCategory, s.Status })
            .FirstOrDefaultAsync(ct);
        if (sched is not null)
        {
            dto.nextReviewDate = sched.NextReviewDate;
            dto.riskCategory   = sched.CurrentRiskCategory;
            dto.reviewStatus   = sched.Status;
        }

        var txnIds = snaps.Where(s => s.TransactionId.HasValue)
                          .Select(s => s.TransactionId!.Value).Distinct().ToList();
        if (txnIds.Count == 0) return dto;

        var txns = await _db.Transactions.AsNoTracking()
            .Where(t => t.TenantId == tenantId && txnIds.Contains(t.TransactionId))
            .Select(t => new { t.TransactionId, t.ProductId, t.TransactionStatusLkp, t.CreatedDate })
            .ToListAsync(ct);

        dto.totalTransactions = txns.Count;
        if (txns.Count == 0) return dto;

        // Product labels
        var productIds = txns.Select(t => t.ProductId).Distinct().ToList();
        var prodNames = (await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName })
                .ToListAsync(ct))
            .ToDictionary(x => x.ProductId, x => x.ProductName);

        // Status labels (APPLICATION_STATUS)
        var statusIds = txns.Select(t => t.TransactionStatusLkp).Distinct().ToList();
        var statusNames = (await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "APPLICATION_STATUS" && statusIds.Contains(l.Id))
                .Select(l => new { l.Id, l.VisibleValue })
                .ToListAsync(ct))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().VisibleValue);

        dto.transactionsOverTime = txns
            .GroupBy(t => t.CreatedDate.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new TimeBucketDto { period = g.Key, count = g.Count() })
            .ToList();

        dto.productMix = txns
            .GroupBy(t => prodNames.TryGetValue(t.ProductId, out var n) && !string.IsNullOrWhiteSpace(n) ? n! : $"Product {t.ProductId}")
            .Select(g => new NamedCountDto { label = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        dto.statusMix = txns
            .GroupBy(t => statusNames.TryGetValue(t.TransactionStatusLkp, out var n) && !string.IsNullOrWhiteSpace(n) ? n! : $"Status {t.TransactionStatusLkp}")
            .Select(g => new NamedCountDto { label = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        return dto;
    }
}
