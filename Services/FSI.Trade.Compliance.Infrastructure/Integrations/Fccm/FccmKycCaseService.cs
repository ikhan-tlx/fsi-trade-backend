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
    private readonly IntegrationOptions      _integration;
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
        _db          = db;
        _http        = http;
        _current     = current;
        _integration = integration.Value;
        _onboarding  = integration.Value.Onboarding;
        _resolver    = resolver;
        _log         = log;
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

        // Verbose diagnostics — summary of the resolved customer + classification
        // fields going to FCCM (identity from the snapshot, classification from the
        // derivation / config defaults, risk-param set gated by IncludeRiskParameterFields).
        {
            var oc = payload.OnboardingCustomer;
            _log.LogDebug(
                "KYC onboarding payload for txn {TxnId}: applicationId={AppId} applicant={Applicant} " +
                "type={Type} jurisdiction={Jur} countryOfResidence={Cor} businessDomain={Bd} " +
                "displayName={Name} riskParamFieldsIncluded={RiskInc}",
                req.TransactionId, payload.applicationId, oc.ApplicantID, oc.CustomerType,
                oc.Jurisdiction, oc.CountryOfResidence, oc.BusinessDomain, oc.DisplayName,
                _onboarding.IncludeRiskParameterFields);
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
        if (string.IsNullOrWhiteSpace(payload.CaseID))
            throw new ConflictException("invalid_callback", "FCCM callback missing caseID.");

        // Load the KycCaseRequest row ONCE (FCCM callback → KYC tracking
        // row → originating transaction id; per FSI direction "we can
        // directly query from the KycCaseRequests to fetch the transaction
        // id"). FccmCaseId is indexed. The result feeds both:
        //   (1) ApplyToKycCaseAsync — mutates the row in place (KYC
        //       lifecycle update).
        //   (2) ApplyToTradeTransactionAsync — uses row.TransactionId to
        //       hit TmX_Transaction.Transaction_Status_Lkp directly. No
        //       JSON_VALUE scan against TmX_Transaction_Detail.UDF_Data.
        //
        // Fallback for Trade-only submissions (Slice 9 path doesn't write
        // a KycCaseRequest row): if the row is null, ApplyToTradeTransactionAsync
        // looks up the transaction by Transaction.FccmCaseId, which Slice 9
        // persists at submission time (indexed by IX_TmX_Transaction_FccmCaseId).
        var caseRow = await _db.KycCaseRequests
            .FirstOrDefaultAsync(x => x.FccmCaseId == payload.CaseID, ct);

        await ApplyToKycCaseAsync(payload, caseRow, ct);
        await ApplyToTradeTransactionAsync(payload, caseRow, ct);
    }

    // -------------------------------------------------------------------
    // (1) KYC case-creation tracking row — original Slice 4 behaviour.
    // -------------------------------------------------------------------
    private async Task ApplyToKycCaseAsync(KycCaseCallback payload, KycCaseRequest? row, CancellationToken ct)
    {
        if (row is null)
        {
            _log.LogDebug("FCCM callback {CaseID} does not match a KycCaseRequest — skipping KYC update.",
                          payload.CaseID);
            return;
        }

        // Map upstream action codes to terminal statuses. 30004 = approve,
        // 30003 = reject. Compared as strings to match the legacy contract
        // (FCCM wires action codes as quoted strings). The Trade-side
        // mapping uses the configurable FccmActionCodeStatusMap; the KYC
        // tracking row is a fixed two-state machine so we keep the
        // hardcoded mapping here.
        var actionCode = (payload.ActionCode ?? "").Trim();
        if (actionCode == "30004")
        {
            row.Status      = KycCaseStatus.RiskAssessed;
            row.ErrorDetail = null;
        }
        else if (actionCode == "30003")
        {
            row.Status      = KycCaseStatus.Failed;
            row.ErrorDetail = string.IsNullOrWhiteSpace(payload.ActionReason) ? "Rejected by FCCM" : payload.ActionReason;
        }
        else
        {
            _log.LogInformation("FCCM callback action {ActionCode} on KYC case {CaseID} — no KYC transition mapped.",
                                payload.ActionCode, payload.CaseID);
            return;
        }

        row.LastUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // -------------------------------------------------------------------
    // (2) Trade transaction verdict — mirrors legacy
    //     updateTransactionStatusAPIController. Matches the callback's
    //     case id against TransactionCode embedded in
    //     TmX_Transaction_Detail.UDF_Data, then writes the resolved
    //     APPLICATION_STATUS lookup id to Transaction.Transaction_Status_Lkp
    //     and drops an audit-trail remark.
    // -------------------------------------------------------------------
    private async Task ApplyToTradeTransactionAsync(KycCaseCallback payload, KycCaseRequest? caseRow, CancellationToken ct)
    {
        // Map action code → APPLICATION_STATUS Visible_Value via config.
        // Action code is a string per the legacy contract; the config map
        // key matches. Any unmapped code (e.g. legacy "back to analyst"
        // path) gets a log line and no status mutation — the FE's analyst
        // inbox will continue to show the transaction in its current
        // workflow state.
        var actionCode = (payload.ActionCode ?? "").Trim();
        if (actionCode.Length == 0
            || !_integration.FccmActionCodeStatusMap.TryGetValue(actionCode, out var targetStatusName)
            || string.IsNullOrWhiteSpace(targetStatusName))
        {
            _log.LogInformation(
                "FCCM callback action {ActionCode} on case {CaseID} — no Trade status mapping configured; skipping Transaction_Status_Lkp update.",
                payload.ActionCode, payload.CaseID);
            return;
        }

        // Resolve TransactionId via the indexed paths — no JSON_VALUE scan.
        //
        // (1) Primary: KycCaseRequest.TransactionId. Populated at KYC
        //     submission time (Slice 4). FccmCaseId index already used by
        //     the caller above; we just read the column.
        //
        // (2) Fallback: Transaction.FccmCaseId direct lookup. Populated by
        //     the Slice 9 Trade-submission pipeline, which doesn't write
        //     a KycCaseRequest row. Indexed by IX_TmX_Transaction_FccmCaseId.
        int? transactionId = null;
        if (caseRow?.TransactionId is long txnId && txnId > 0)
        {
            transactionId = (int)txnId;
        }
        else
        {
            var caseId = payload.CaseID!;
            transactionId = await _db.Transactions.AsNoTracking()
                .Where(t => t.FccmCaseId == caseId)
                .Select(t => (int?)t.TransactionId)
                .FirstOrDefaultAsync(ct);
        }

        if (transactionId is null)
        {
            _log.LogDebug("FCCM callback {CaseID} does not match a Trade transaction — skipping status update.",
                          payload.CaseID);
            return;
        }

        // Resolve the target lookup row by (LOOKUP_TYPE='APPLICATION_STATUS',
        // Visible_Value=name). Cross-tenant cascade: prefer caller's tenant
        // when context exists, else fall back to Tenant 1.
        var callerTenantId = _current.TenantId ?? 1;

        var statusLkpId = await ResolveApplicationStatusLookupAsync(
            targetStatusName, callerTenantId, ct);

        if (statusLkpId is null)
        {
            _log.LogWarning(
                "FCCM callback {CaseID}: APPLICATION_STATUS Visible_Value='{Status}' not found in TmX_Lookup; cannot update transaction {TransactionId}.",
                payload.CaseID, targetStatusName, transactionId);
            return;
        }

        // Load the transaction row tracked so we can mutate + SaveChanges.
        var txId = transactionId.Value;
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == txId, ct);
        if (tx is null)
        {
            _log.LogWarning(
                "FCCM callback {CaseID}: TransactionDetail pointed at Transaction {TransactionId} which no longer exists.",
                payload.CaseID, txId);
            return;
        }

        var previous = tx.TransactionStatusLkp;
        tx.TransactionStatusLkp = statusLkpId.Value;
        tx.LastUpdatedBy        = "FCCM Callback";
        tx.LastUpdatedDate      = DateTime.UtcNow;

        // Audit-trail remark so the verdict shows on the Transaction Detail
        // Page's Audit Trail tab + Notes list. Module_Code "5" = Transaction
        // (matches GetTransactionByIdQueryHandler / GetAuditTrailQueryHandler).
        var remark = new Domain.Entities.Application.ApplicationRemark
        {
            TransactionId = txId,
            ModuleCode    = "5",
            TenantId      = tx.TenantId,
            ActionType    = $"FCCM:{payload.ActionCode}",
            RemarksLkp    = 0,
            Comments      = string.IsNullOrWhiteSpace(payload.ActionReason)
                                ? $"FCCM verdict received → {targetStatusName} (prev status lkp = {previous})"
                                : $"FCCM verdict → {targetStatusName}: {payload.ActionReason}",
            UserId        = "fccm-callback",
            CreatedBy     = "FCCM Callback",
            CreatedDate   = DateTime.UtcNow
        };
        _db.ApplicationRemarks.Add(remark);

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "FCCM callback {CaseID}: transaction {TransactionId} Transaction_Status_Lkp {Previous} → {New} ({StatusName}).",
            payload.CaseID, txId, previous, statusLkpId.Value, targetStatusName);
    }

    /// <summary>
    /// Resolves an APPLICATION_STATUS row to its Lookup_Id with the same
    /// tenant cascade used by GetLookupsByCultureQueryHandler:
    /// caller's tenant first, fall back to Tenant 1, else lowest tenant
    /// that has a matching row. Returns null on no match.
    /// </summary>
    private async Task<int?> ResolveApplicationStatusLookupAsync(
        string            visibleValue,
        int               callerTenantId,
        CancellationToken ct)
    {
        const string applicationStatusType = "APPLICATION_STATUS";
        const int    platformDefaultTenant = 1;

        var candidates = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == applicationStatusType
                     && l.VisibleValue != null
                     && l.VisibleValue == visibleValue
                     && l.IsActive == true)
            .Select(l => new { l.Id, l.TenantId })
            .ToListAsync(ct);

        if (candidates.Count == 0) return null;

        // Prefer caller's tenant.
        var hit = candidates.FirstOrDefault(x => x.TenantId == callerTenantId);
        if (hit is not null) return hit.Id;

        // Then platform default.
        hit = candidates.FirstOrDefault(x => x.TenantId == platformDefaultTenant);
        if (hit is not null) return hit.Id;

        // Then lowest tenant with data.
        return candidates.OrderBy(x => x.TenantId).First().Id;
    }
}
