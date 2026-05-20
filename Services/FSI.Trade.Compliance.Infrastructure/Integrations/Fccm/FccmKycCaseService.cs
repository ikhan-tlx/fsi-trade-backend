using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// FCCM-backed implementation of <see cref="IKycCaseService"/>. Composes:
///   • <see cref="FccmHttpClient"/> for case submission.
///   • <see cref="IApplicationDbContext"/> for the local KycCaseRequest CRUD.
///
/// The Oracle-side polling lives in <see cref="FccmCaseIdPoller"/>
/// (a hosted service) — this class doesn't poll; it just submits and reads.
/// </summary>
public class FccmKycCaseService : IKycCaseService
{
    private readonly IApplicationDbContext   _db;
    private readonly FccmHttpClient          _http;
    private readonly ICurrentUserService     _current;
    private readonly ILogger<FccmKycCaseService> _log;

    public FccmKycCaseService(
        IApplicationDbContext       db,
        FccmHttpClient              http,
        ICurrentUserService         current,
        ILogger<FccmKycCaseService> log)
    {
        _db      = db;
        _http    = http;
        _current = current;
        _log     = log;
    }

    public async Task<KycCaseSubmissionResult> SubmitAsync(KycCaseSubmissionRequest req, CancellationToken ct = default)
    {
        var now    = DateTime.UtcNow;
        var actor  = _current.UserName ?? _current.UserId ?? "unknown";

        // 1. Insert the local record in Submitted state. Returns immediately to FE.
        var entity = new KycCaseRequest
        {
            TenantId      = _current.TenantId ?? 1,
            CustomerId    = req.CustomerId,
            TransactionId = req.TransactionId,
            SubmittedBy   = actor,
            SubmittedAt   = now,
            Status        = KycCaseStatus.Submitted,
            LastUpdatedAt = now
        };
        _db.KycCaseRequests.Add(entity);
        await _db.SaveChangesAsync(ct);

        // 2. Fire HTTP submit to upstream. If it fails, mark Failed and surface
        //    the error — the FE will see Status=Failed on its first poll.
        string? upstreamRequestId;
        try
        {
            upstreamRequestId = await _http.SubmitCaseAsync(req.CustomerId, req.TransactionId, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "FCCM submit threw for KycCaseRequest {RequestId}.", entity.Id);
            entity.Status        = KycCaseStatus.Failed;
            entity.ErrorDetail   = $"Submit threw: {ex.Message}";
            entity.LastUpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new KycCaseSubmissionResult { RequestId = entity.Id, Status = entity.Status };
        }

        if (string.IsNullOrWhiteSpace(upstreamRequestId))
        {
            entity.Status        = KycCaseStatus.Failed;
            entity.ErrorDetail   = "Upstream returned no requestId.";
            entity.LastUpdatedAt = DateTime.UtcNow;
        }
        else
        {
            entity.FccmRequestId = upstreamRequestId;
            entity.Status        = KycCaseStatus.AwaitingCaseId;
            entity.LastUpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return new KycCaseSubmissionResult { RequestId = entity.Id, Status = entity.Status };
    }

    public async Task<KycCaseStatusDto?> GetStatusAsync(long requestId, CancellationToken ct = default)
    {
        var row = await _db.KycCaseRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (row is null) return null;

        return new KycCaseStatusDto
        {
            RequestId       = row.Id,
            Status          = row.Status,
            FccmCaseId      = row.FccmCaseId,
            RiskCategoryKey = row.RiskCategoryKey,
            SubmittedAt     = row.SubmittedAt,
            LastPolledAt    = row.LastPolledAt,
            ErrorDetail     = row.ErrorDetail
        };
    }

    public async Task HandleCallbackAsync(KycCaseCallback payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payload.FccmCaseId))
            throw new ConflictException("invalid_callback", "FCCM callback missing fccmCaseId.");

        var row = await _db.KycCaseRequests
            .FirstOrDefaultAsync(x => x.FccmCaseId == payload.FccmCaseId, ct);
        if (row is null)
        {
            _log.LogWarning("FCCM callback for unknown fccmCaseId {FccmCaseId} — ignoring.", payload.FccmCaseId);
            return;
        }

        // Map upstream action codes to terminal statuses. Reason text optional.
        // 30004 = approve → RiskAssessed
        // 30003 = reject  → Failed (with reason)
        if (payload.ActionCode == 30004)
        {
            row.Status      = KycCaseStatus.RiskAssessed;
            row.ErrorDetail = null;
        }
        else if (payload.ActionCode == 30003)
        {
            row.Status      = KycCaseStatus.Failed;
            row.ErrorDetail = string.IsNullOrWhiteSpace(payload.ActionReason) ? "Rejected by FCCM" : payload.ActionReason;
        }
        else
        {
            _log.LogInformation("FCCM callback action {ActionCode} on case {CaseId} — no state transition mapped.",
                                payload.ActionCode, payload.FccmCaseId);
            return;
        }

        row.LastUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
