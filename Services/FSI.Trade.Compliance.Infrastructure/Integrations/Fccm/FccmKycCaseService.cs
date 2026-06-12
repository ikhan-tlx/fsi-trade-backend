using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly FccmOnboardingOptions   _onboarding;
    private readonly KycOnboardingFieldResolver _resolver;
    private readonly ILogger<FccmKycCaseService> _log;

    public FccmKycCaseService(
        IApplicationDbContext         db,
        FccmHttpClient                http,
        ICurrentUserService           current,
        IOptions<IntegrationOptions>  integration,
        KycOnboardingFieldResolver    resolver,
        ILogger<FccmKycCaseService>   log)
    {
        _db         = db;
        _http       = http;
        _current    = current;
        _onboarding = integration.Value.Onboarding;
        _resolver   = resolver;
        _log        = log;
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

        // 2. Build the onboarding payload from the transaction's customer
        //    snapshot (legacy KYCRequestModel parity). Customer-identity +
        //    classification fields are populated here; the risk-parameter and
        //    multi-value-grid-derived fields remain a documented follow-up
        //    (they need the KYC Section-A capture + the FCCM Oracle
        //    FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score lookup).
        var snap = await _db.TransactionCustomerSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransactionId == req.TransactionId, ct);
        var applicantId = snap?.CustomerCode ?? req.CustomerId;
        var name = snap?.CustomerName ?? applicantId;

        // All static defaults + field-inclusion toggles come from
        // Integration:Onboarding (FccmOnboardingOptions) — switching FCCM
        // environments (e.g. legacy REQUIRES CustomerName, current env REJECTS
        // it) is a config change, not a code edit. Null fields are omitted on
        // the wire (FccmHttpClient.WireJson), so a disabled toggle drops the key.
        var fmt = string.IsNullOrWhiteSpace(_onboarding.ApplicationIdFormat)
            ? "{0}" : _onboarding.ApplicationIdFormat;
        var payload = new FccmOnboardingPayload
        {
            applicationId = string.Format(fmt, applicantId),
            requestUserId = actor,
            OnboardingCustomer = new FccmOnboardingCustomer
            {
                ApplicantID        = applicantId,
                CustomerType       = _onboarding.CustomerType,
                Jurisdiction       = _onboarding.Jurisdiction,
                CountryOfResidence = _onboarding.CountryOfResidence,
                BusinessDomain     = _onboarding.BusinessDomain,
                DisplayName        = name,
                OrganizationName   = name,
                // CustomerName: legacy schema requires it; current env rejects
                // it (HTTP 400). Toggled via IncludeCustomerName.
                CustomerName       = _onboarding.IncludeCustomerName ? name : null,
                // Name parts — the verified sample sends FirstName + empty
                // Last/Middle even for an org. Toggled via IncludeNameParts.
                FirstName          = _onboarding.IncludeNameParts ? name : null,
                LastName           = _onboarding.IncludeNameParts ? "" : null,
                MiddleName         = _onboarding.IncludeNameParts ? "" : null,
            },
        };

        // 2b. Legacy parity — derive the risk-parameter / classification fields
        //     from the KYC grids (KycOnboardingFieldResolver mirrors the legacy
        //     SetRealtimeValues: hidden-value resolution + the Oracle
        //     FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score lookup). Best-effort: a
        //     null derivation leaves the configured static fallback in place.
        if (req.TransactionId is long txnIdRaw)
        {
            var d = await _resolver.ResolveAsync((int)txnIdRaw, ct);
            var oc = payload.OnboardingCustomer;

            // Toggle-OFF safe overlay: only fields present in the verified
            // sample the strict env accepts (CountryOfResidence + OrganizationName).
            // Overlay the derived value when present, else keep the static default.
            // Everything else is gated — the strict env rejects unknown properties
            // (it 400'd on CustomerName), and Industry / CountryOfIncorporation /
            // OrganizationLegalStructure / RelationshipTeamCode / TypeofRequest are
            // NOT in the proven sample, so they must not be sent by default.
            oc.CountryOfResidence = d.CountryOfResidence ?? oc.CountryOfResidence;
            oc.OrganizationName   = d.OrganizationName   ?? oc.OrganizationName;

            // Full legacy risk-parameter / classification set — only emitted when
            // the target environment's schema accepts it (IncludeRiskParameterFields).
            if (_onboarding.IncludeRiskParameterFields)
            {
                oc.Industry                   = d.Industry;
                oc.OrganizationLegalStructure = d.OrganizationLegalStructure;
                oc.CountryOfIncorporation     = d.CountryOfIncorporation;
                oc.RelationshipTeamCode       = d.RelationshipTeamCode;
                oc.TypeofRequest              = d.TypeofRequest;
                oc.EmploymentType       = d.EmploymentType;
                oc.CountryOfBirth       = d.CountryOfBirth;
                oc.WealthSource         = d.WealthSource;
                oc.EmployerName         = d.EmployerName;
                oc.CitizenshipStatus    = d.CitizenshipStatus;
                oc.PrimaryCitizenship   = d.PrimaryCitizenship;
                oc.SecondaryCitizenship = d.SecondaryCitizenship;
                oc.CustomerNAICSCode    = d.CustomerNAICSCode;
                oc.Occupation           = d.Occupation;
                oc.NumberOfDependents   = d.NumberOfDependents;
                oc.JobTitle             = d.JobTitle;
                oc.Age                  = d.Age;
                oc.RetirementYear       = d.RetirementYear;
                oc.EmploymentStatus     = d.EmploymentStatus;
                oc.Alias                = d.Alias;
                oc.CUST_PERF            = d.CustPerf;
                oc.DEF_SECT             = d.DefSect;
                oc.HS_CD                = d.HsCode;
                oc.StructBus            = d.StructBus;
                oc.OVER_IMP             = d.OverImp;
            }
        }

        // 3. Fire HTTP submit to upstream. If it fails, mark Failed and surface
        //    the upstream error detail — the FE will see Status=Failed + reason.
        string? upstreamRequestId;
        string? submitError;
        try
        {
            (upstreamRequestId, submitError) = await _http.SubmitCaseAsync(payload, req.CustomerId, ct);
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
            entity.ErrorDetail   = submitError ?? "Upstream returned no requestId.";
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
