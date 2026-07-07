using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Application.Features.Kyc.RiskAssessment;

/// <summary>
/// Assembles <see cref="KycRiskAssessmentDto"/> from the local case + comparison
/// data — one row per KYC submission (KycCaseRequest) for the transaction's
/// customer, newest first, enriched with the matching Cstm_Brains reconciliation
/// row and the originating transaction's number/product. No upstream calls.
/// </summary>
public class GetKycRiskAssessmentQueryHandler
    : IRequestHandler<GetKycRiskAssessmentQuery, KycRiskAssessmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IntegrationOptions    _integration;

    public GetKycRiskAssessmentQueryHandler(
        IApplicationDbContext db,
        IOptions<IntegrationOptions> integration)
    {
        _db          = db;
        _integration = integration.Value;
    }

    public async Task<KycRiskAssessmentDto> Handle(GetKycRiskAssessmentQuery req, CancellationToken ct)
    {
        // Resolve the customer for the opened transaction from its snapshot.
        var snap = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.TransactionId == req.TransactionId)
            .OrderByDescending(c => c.CustomerMasterId)
            .Select(c => new { c.CustomerCode, c.CustomerName })
            .FirstOrDefaultAsync(ct);

        if (snap is null)
            throw new NotFoundException("transaction_not_found",
                $"Transaction {req.TransactionId} has no customer snapshot.");

        var dto = new KycRiskAssessmentDto
        {
            CustomerCode           = snap.CustomerCode,
            CustomerName           = snap.CustomerName,
            PollingIntervalSeconds = Math.Max(1, (int)_integration.PollingInterval.TotalSeconds),
            CaseIdTimeoutMinutes   = Math.Max(1, (int)_integration.CaseIdTimeout.TotalMinutes),
            CanOperate             = true, // FE gates the ⋮ menu on its own Kyc.Operate check; endpoints enforce server-side.
        };

        var customerCode = snap.CustomerCode;
        if (string.IsNullOrWhiteSpace(customerCode))
            return dto; // no code → cannot resolve history; empty state.

        // All KYC executions for this customer, newest first.
        var rows = await _db.KycCaseRequests.AsNoTracking()
            .Where(k => k.CustomerId == customerCode)
            .OrderByDescending(k => k.SubmittedAt)
            .ToListAsync(ct);

        if (rows.Count == 0) return dto;

        // Enrichment: Cstm_Brains by transaction id; transaction number + product label.
        var txnIds = rows.Where(r => r.TransactionId.HasValue)
                         .Select(r => (int)r.TransactionId!.Value).Distinct().ToList();

        var cstmByTxn = (await _db.CstmBrains.AsNoTracking()
                .Where(c => txnIds.Contains(c.TransactionId))
                .ToListAsync(ct))
            .GroupBy(c => c.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        var txnInfo = (await (
                from t in _db.Transactions.AsNoTracking()
                where txnIds.Contains(t.TransactionId)
                join p in _db.Products.AsNoTracking() on t.ProductId equals p.ProductId into pj
                from p in pj.DefaultIfEmpty()
                select new { t.TransactionId, t.TransactionNumber, ProductName = p != null ? p.ProductName : null })
            .ToListAsync(ct))
            .ToDictionary(x => x.TransactionId, x => x);

        // Pre-reconcile BRAINS band fallback: the create-time snapshot RiskScore
        // (numeric) captured from BRAINS before FCCM reconciliation.
        var createBandByTxn = (await _db.TransactionCustomerSnapshots.AsNoTracking()
                .Where(c => c.TransactionId.HasValue && txnIds.Contains(c.TransactionId!.Value) && c.UdfData != null)
                .Select(c => new { c.TransactionId, c.UdfData })
                .ToListAsync(ct))
            .GroupBy(c => c.TransactionId!.Value)
            .ToDictionary(g => g.Key, g => ExtractCreateBrainsScore(g.First().UdfData));

        foreach (var r in rows)
        {
            var txnKey = r.TransactionId.HasValue ? (int)r.TransactionId.Value : (int?)null;
            cstmByTxn.TryGetValue(txnKey ?? -1, out var cstm);
            txnInfo.TryGetValue(txnKey ?? -1, out var tx);

            // BRAINS: reconciled Brains_Score if present, else create-time snapshot band.
            int? brainsScore = KycRiskBands.ParseScore(cstm?.BrainsScore);
            if (brainsScore is null && txnKey is int bk && createBandByTxn.TryGetValue(bk, out var cb))
                brainsScore = cb;

            // FCCM: reconciled FccmScore, else parse Risk_Category_Key.
            int? fccmScore = KycRiskBands.ParseScore(cstm?.FccmScore) ?? KycRiskBands.ParseScore(r.RiskCategoryKey);

            int? finalScore = KycRiskBands.ParseScore(cstm?.FinalScore);

            dto.Executions.Add(new KycRiskExecutionDto
            {
                RequestId  = r.Id,
                RequestRef = $"KYC-REQ-{r.Id:00000}",

                TransactionId        = r.TransactionId,
                TransactionNumber    = tx?.TransactionNumber,
                ProductLabel         = tx?.ProductName,
                IsCurrentTransaction = r.TransactionId == req.TransactionId,

                SubmittedAt = r.SubmittedAt,
                SubmittedBy = r.SubmittedBy,

                Brains = new KycRiskBandDto { Score = brainsScore, Label = KycRiskBands.BrainsLabel(brainsScore) },
                Fccm   = new KycRiskBandDto { Score = fccmScore,   Label = KycRiskBands.FccmLabel(fccmScore) },
                Reconciled = new KycReconciledRiskDto
                {
                    Score            = finalScore,
                    Label            = KycRiskBands.FinalLabel(finalScore),
                    Display          = cstm?.Display,
                    PrevailingSource = KycRiskBands.PrevailingSource(cstm?.Display),
                },

                FccmCaseId    = r.FccmCaseId,
                FccmRequestId = r.FccmRequestId,

                Status      = r.Status,
                ErrorDetail = r.ErrorDetail,

                Lifecycle = new KycLifecycleDto
                {
                    SubmittedAt    = r.SubmittedAt,
                    CaseCreatedAt  = r.CaseCreatedAt,
                    RiskAssessedAt = r.RiskAssessedAt,
                    LastPolledAt   = r.LastPolledAt,
                    LastUpdatedAt  = r.LastUpdatedAt,
                },
            });
        }

        // Default-selected "current" = the execution for THIS transaction, else newest.
        dto.CurrentRequestId =
            dto.Executions.FirstOrDefault(e => e.IsCurrentTransaction)?.RequestId
            ?? dto.Executions.FirstOrDefault()?.RequestId;

        return dto;
    }

    /// <summary>
    /// Reads the create-time BRAINS band from a customer snapshot's UDF_Data
    /// <c>$.RiskScore</c>. At create it's a numeric band (0-3); the poller later
    /// overwrites it with the reconciled display string — in that case there's a
    /// Cstm_Brains row so this fallback isn't used. Returns null for a non-numeric
    /// (already-reconciled) value.
    /// </summary>
    private static int? ExtractCreateBrainsScore(string? udf)
    {
        if (string.IsNullOrWhiteSpace(udf)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(udf);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("RiskScore", out var rs))
            {
                if (rs.ValueKind == System.Text.Json.JsonValueKind.Number && rs.TryGetInt32(out var n)) return n;
                if (rs.ValueKind == System.Text.Json.JsonValueKind.String
                    && int.TryParse(rs.GetString(), out var sn)) return sn;
            }
        }
        catch { /* malformed → no fallback */ }
        return null;
    }
}
