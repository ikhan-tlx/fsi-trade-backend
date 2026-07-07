using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// On-demand operational actions for the Risk Assessment tab (privileged
/// <c>Kyc.Operate</c>). Reuses the exact same Oracle reads + reconciliation the
/// background <see cref="FccmCaseIdPoller"/> runs — the poller's
/// <c>ReconcileRiskAsync</c> is shared as an internal static so re-poll/re-fetch
/// stay byte-for-byte consistent with the automatic path. All actions are
/// audited via the standard SaveChanges audit interceptor + structured logs.
/// </summary>
public class KycCaseOperationsService : IKycCaseOperationsService
{
    private readonly IApplicationDbContext  _db;
    private readonly FccmOracleReader       _oracle;
    private readonly IKycScreeningService   _kycScreening;
    private readonly IKycCaseService        _cases;
    private readonly IntegrationOptions     _opt;
    private readonly ILogger<KycCaseOperationsService> _log;

    public KycCaseOperationsService(
        IApplicationDbContext db,
        FccmOracleReader      oracle,
        IKycScreeningService  kycScreening,
        IKycCaseService       cases,
        IOptions<IntegrationOptions> opt,
        ILogger<KycCaseOperationsService> log)
    {
        _db           = db;
        _oracle       = oracle;
        _kycScreening = kycScreening;
        _cases        = cases;
        _opt          = opt.Value;
        _log          = log;
    }

    public async Task<KycCaseSubmissionResult> RetryAsync(long requestId, CancellationToken ct = default)
    {
        // Retry re-fires onboarding ON THE SAME row/transaction (transient-failure
        // recovery) — it does NOT open a new submission or transaction. A genuine
        // re-assessment of a customer is a new KYC transaction (analyst flow),
        // which produces its own execution row in the history. Guard + reset live
        // in ResubmitAsync so first-submit and retry share the exact fire path.
        _log.LogInformation("KYC operate RETRY requested for case {RequestId}.", requestId);
        return await _cases.ResubmitAsync(requestId, ct);
    }

    public async Task<KycCaseStatusDto> RepollAsync(long requestId, CancellationToken ct = default)
    {
        var row = await _db.KycCaseRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct)
                  ?? throw new NotFoundException("kyc_case_not_found", $"KYC case {requestId} not found.");

        var inFlight = row.Status is KycCaseStatus.Submitted or KycCaseStatus.AwaitingCaseId or KycCaseStatus.CaseCreated;
        if (!inFlight)
            throw new ConflictException("kyc_repoll_not_allowed",
                $"Re-poll is only available while an execution is in flight (current: {row.Status}).");

        var now    = DateTime.UtcNow;
        var cutoff = now - _opt.CaseIdTimeout;
        row.LastPolledAt = now;

        // Phase A — provision the FCCM case id (mirrors FccmCaseIdPoller Phase A).
        if (row.Status == KycCaseStatus.AwaitingCaseId && !string.IsNullOrWhiteSpace(row.FccmRequestId))
        {
            var caseId = await _oracle.ReadCaseIdAsync(row.FccmRequestId!, ct);
            if (!string.IsNullOrWhiteSpace(caseId))
            {
                row.FccmCaseId    = caseId;
                row.Status        = KycCaseStatus.CaseCreated;
                row.CaseCreatedAt = now;
                row.LastUpdatedAt = now;
                _log.LogInformation("KYC operate RE-POLL: request {RequestId} → CaseCreated ({CaseId}).", row.Id, caseId);
            }
            else if (row.SubmittedAt < cutoff)
            {
                row.Status        = KycCaseStatus.Timeout;
                row.ErrorDetail   = $"FCCM CASE_ID not provisioned within {_opt.CaseIdTimeout}.";
                row.LastUpdatedAt = now;
            }
        }

        // Phase B — risk category + reconcile (mirrors FccmCaseIdPoller Phase B).
        if (row.Status == KycCaseStatus.CaseCreated && row.RiskCategoryKey == null
            && !string.IsNullOrWhiteSpace(row.FccmRequestId))
        {
            var risk = await _oracle.ReadRiskCategoryKeyAsync(row.FccmRequestId!, ct);
            if (!string.IsNullOrWhiteSpace(risk))
            {
                row.RiskCategoryKey = risk;
                row.Status          = KycCaseStatus.RiskAssessed;
                row.RiskAssessedAt  = now;
                row.LastUpdatedAt   = now;
                await FccmCaseIdPoller.ReconcileRiskAsync(_db, _oracle, _kycScreening, row, risk!, now, _log, ct);
                _log.LogInformation("KYC operate RE-POLL: request {RequestId} → RiskAssessed ({Risk}).", row.Id, risk);
            }
        }

        await _db.SaveChangesAsync(ct);
        return ToStatusDto(row);
    }

    public async Task<KycCaseStatusDto> RefreshAsync(long requestId, CancellationToken ct = default)
    {
        var row = await _db.KycCaseRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct)
                  ?? throw new NotFoundException("kyc_case_not_found", $"KYC case {requestId} not found.");

        if (string.IsNullOrWhiteSpace(row.FccmRequestId))
            throw new ConflictException("kyc_refresh_not_allowed",
                "Re-fetch requires a submitted case (no FCCM request id yet).");

        var now = DateTime.UtcNow;
        row.LastPolledAt = now;

        var risk = await _oracle.ReadRiskCategoryKeyAsync(row.FccmRequestId!, ct);
        if (!string.IsNullOrWhiteSpace(risk))
        {
            row.RiskCategoryKey = risk;
            if (row.Status != KycCaseStatus.RiskAssessed)
            {
                row.Status         = KycCaseStatus.RiskAssessed;
                row.RiskAssessedAt = row.RiskAssessedAt ?? now;
            }
            row.LastUpdatedAt = now;
            await FccmCaseIdPoller.ReconcileRiskAsync(_db, _oracle, _kycScreening, row, risk!, now, _log, ct);
            _log.LogInformation("KYC operate RE-FETCH: request {RequestId} risk category re-read ({Risk}) + reconciled.", row.Id, risk);
        }
        else
        {
            _log.LogInformation("KYC operate RE-FETCH: request {RequestId} — FCCM has no risk category yet; nothing to reconcile.", row.Id);
        }

        await _db.SaveChangesAsync(ct);
        return ToStatusDto(row);
    }

    private static KycCaseStatusDto ToStatusDto(KycCaseRequest row) => new()
    {
        RequestId       = row.Id,
        Status          = row.Status,
        FccmCaseId      = row.FccmCaseId,
        RiskCategoryKey = row.RiskCategoryKey,
        SubmittedAt     = row.SubmittedAt,
        LastPolledAt    = row.LastPolledAt,
        ErrorDetail     = row.ErrorDetail,
    };
}
