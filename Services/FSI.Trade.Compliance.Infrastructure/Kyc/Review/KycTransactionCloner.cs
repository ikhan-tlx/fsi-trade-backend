using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Transactions.Create;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

public class KycTransactionCloner : IKycTransactionCloner
{
    private const string Actor = "PERIODIC REVIEWER";
    private readonly IApplicationDbContext _db;
    public KycTransactionCloner(IApplicationDbContext db) => _db = db;

    public async Task<CreateTransactionCommand> BuildCreateCommandAsync(int sourceTransactionId, CancellationToken ct = default)
    {
        var tx = await _db.Transactions.AsNoTracking()
                     .FirstOrDefaultAsync(t => t.TransactionId == sourceTransactionId, ct)
                 ?? throw new NotFoundException("source_txn_not_found", $"Source transaction {sourceTransactionId} not found.");

        var snap = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TransactionId == sourceTransactionId, ct);

        return new CreateTransactionCommand
        {
            productId             = tx.ProductId,
            companyBranchId       = tx.CompanyBranchId ?? 0,
            transactionDate       = DateTime.UtcNow,
            clientReferenceNumber = tx.ClientReferenceNumber,
            transactionTypeLkp    = tx.TransactionTypeLkp,
            currencyId            = tx.CurrencyLkp,
            subProductTabId       = tx.SubProductTabId,
            customer = new CreateCustomerInput
            {
                customerCode            = snap?.CustomerCode,
                customerName            = snap?.CustomerName,
                nationalIdentifierValue = snap?.NationalIdentifierValue,
                nationalIdTypeLkp       = snap?.NationalIdTypeLkp,
                customerTypeLkp         = snap?.CustomerTypeLkp,
                // riskScore left null: the full customer udf_data (incl. RiskScore) is
                // copied verbatim by CopyProductDataAsync after create.
            }
        };
    }

    public async Task CopyProductDataAsync(int sourceTransactionId, int newTransactionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Customer snapshot udf_data (rich Section-A). Create wrote only a minimal
        // snapshot; overwrite it with the source's full udf verbatim.
        var srcUdf = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(s => s.TransactionId == sourceTransactionId)
            .Select(s => s.UdfData).FirstOrDefaultAsync(ct);
        if (srcUdf is not null)
        {
            var newSnap = await _db.TransactionCustomerSnapshots
                .FirstOrDefaultAsync(s => s.TransactionId == newTransactionId, ct);
            if (newSnap is not null) newSnap.UdfData = srcUdf;
        }

        // Transaction detail (main dynamic-form UDF) — not created on create; clone it.
        var srcDetail = await _db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == sourceTransactionId).FirstOrDefaultAsync(ct);
        if (srcDetail is not null)
            _db.TransactionDetails.Add(new TransactionDetail
            {
                TransactionId = newTransactionId, TenantId = srcDetail.TenantId,
                UdfData = srcDetail.UdfData, CreatedBy = Actor, CreatedDate = now,
            });

        // Beneficiary + stakeholder grids.
        var bens = await _db.BeneficiaryDetails.AsNoTracking()
            .Where(b => b.TransactionId == sourceTransactionId).ToListAsync(ct);
        foreach (var b in bens)
            _db.BeneficiaryDetails.Add(new BeneficiaryDetail
            {
                TransactionId = newTransactionId, TenantId = b.TenantId,
                UdfData = b.UdfData, CreatedBy = Actor, CreatedDate = now,
            });

        var stks = await _db.TransactionStakeholders.AsNoTracking()
            .Where(s => s.TransactionId == sourceTransactionId).ToListAsync(ct);
        foreach (var s in stks)
            _db.TransactionStakeholders.Add(new TransactionStakeholder
            {
                TransactionId = newTransactionId, TenantId = s.TenantId,
                UdfData = s.UdfData, CreatedBy = Actor, CreatedDate = now,
            });

        await _db.SaveChangesAsync(ct);
    }
}
