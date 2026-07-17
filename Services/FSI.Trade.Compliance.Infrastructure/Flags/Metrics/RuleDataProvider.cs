using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>Compute-on-read historical data access for cross-transaction aggregates.
/// Groups by <c>Customer_Code</c> resolved from <c>TmX_Customer_Master</c> (one snapshot
/// row per transaction). No fact table — every call is a scoped, indexed query.</summary>
public sealed class RuleDataProvider : IRuleDataProvider
{
    private readonly IApplicationDbContext _db;
    public RuleDataProvider(IApplicationDbContext db) => _db = db;

    public async Task<string?> ResolveCustomerCodeAsync(int transactionId, CancellationToken ct)
        => await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(s => s.TransactionId == transactionId && s.CustomerCode != null)
            .Select(s => s.CustomerCode)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CustomerTxnRow>> GetCustomerWindowAsync(
        string customerCode, DateTime fromInclusive, DateTime toInclusive, CancellationToken ct)
    {
        var txnIds = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(s => s.CustomerCode == customerCode && s.TransactionId != null)
            .Select(s => s.TransactionId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (txnIds.Count == 0) return Array.Empty<CustomerTxnRow>();

        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.TransactionId))
            .Select(t => new { t.TransactionId, t.ProductId, t.SubProductTabId, Vd = t.TransactionDate ?? t.CreatedDate })
            .Where(x => x.Vd >= fromInclusive && x.Vd <= toInclusive)
            .ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<CustomerTxnRow>();

        var ids = rows.Select(r => r.TransactionId).ToList();
        var udfs = await _db.TransactionDetails.AsNoTracking()
            .Where(d => ids.Contains(d.TransactionId))
            .Select(d => new { d.TransactionId, d.UdfData })
            .ToListAsync(ct);
        var udfMap = udfs
            .GroupBy(u => u.TransactionId)
            .ToDictionary(g => g.Key, g => g.First().UdfData);

        return rows
            .Select(r => new CustomerTxnRow(
                r.TransactionId, r.Vd, r.ProductId, r.SubProductTabId,
                udfMap.TryGetValue(r.TransactionId, out var u) ? u : null))
            .ToList();
    }

    public async Task<DateTime?> GetLastActivityDateBeforeAsync(
        string customerCode, DateTime beforeExclusive, int excludeTransactionId, CancellationToken ct)
    {
        var txnIds = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(s => s.CustomerCode == customerCode
                        && s.TransactionId != null
                        && s.TransactionId != excludeTransactionId)
            .Select(s => s.TransactionId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (txnIds.Count == 0) return null;

        var dates = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.TransactionId))
            .Select(t => (DateTime?)(t.TransactionDate ?? t.CreatedDate))
            .Where(d => d < beforeExclusive)
            .ToListAsync(ct);
        return dates.Count == 0 ? null : dates.Max();
    }

    public async Task<IReadOnlyList<string>> GetAmountFieldCodesAsync(
        int productId, int? tabId, CancellationToken ct)
        => await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId == productId
                        && (tabId == null || f.TabId == tabId)
                        && f.IsAmountField
                        && f.FieldName != null)
            .Select(f => f.FieldName!)
            .Distinct()
            .ToListAsync(ct);

    public async Task<string?> GetLatestKycUdfAsync(string customerCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerCode)) return null;

        // KYC-product ids (config-driven — product whose name mentions KYC).
        var kycProductIds = await _db.Products.AsNoTracking()
            .Where(p => p.ProductName != null && p.ProductName.Contains("KYC"))
            .Select(p => p.ProductId).ToListAsync(ct);
        if (kycProductIds.Count == 0) return null;

        var txnIds = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(s => s.CustomerCode == customerCode && s.TransactionId != null)
            .Select(s => s.TransactionId!.Value).Distinct().ToListAsync(ct);
        if (txnIds.Count == 0) return null;

        // The customer's most recent KYC transaction.
        var latestId = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.TransactionId) && kycProductIds.Contains(t.ProductId))
            .OrderByDescending(t => t.TransactionDate ?? t.CreatedDate)
            .Select(t => (int?)t.TransactionId).FirstOrDefaultAsync(ct);
        if (latestId is null) return null;

        return await _db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == latestId.Value)
            .Select(d => d.UdfData).FirstOrDefaultAsync(ct);
    }
}
