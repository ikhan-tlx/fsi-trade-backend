using System.Text.Json.Nodes;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// Background service that drives <see cref="KycCaseRequest"/> rows through
/// their state machine without blocking any HTTP request thread. Replaces
/// the legacy 20-second blocking poll inside
/// <c>caseInsertionController.GetCaseIdByRequestId</c>.
///
/// One tick per <see cref="IntegrationOptions.PollingInterval"/>:
///   1. Pick rows in <c>AwaitingCaseId</c> that haven't been polled too
///      recently (avoid hammering Oracle on the same row mid-tick).
///   2. For each, ask <see cref="FccmOracleReader.ReadCaseIdAsync"/>.
///      If non-null → flip to <c>CaseCreated</c>.
///      If null + submitted &gt; <see cref="IntegrationOptions.CaseIdTimeout"/>
///      ago → flip to <c>Timeout</c>.
///   3. Pick rows in <c>CaseCreated</c>; ask
///      <see cref="FccmOracleReader.ReadRiskCategoryKeyAsync"/>.
///      If non-null → flip to <c>RiskAssessed</c>.
///
/// All state transitions are append-only on the existing row plus
/// LastUpdatedAt = now. No external systems are notified directly —
/// downstream consumers (workflow scheme actions) read state on next
/// invocation.
/// </summary>
public class FccmCaseIdPoller : BackgroundService
{
    private readonly IServiceProvider           _sp;
    private readonly IntegrationOptions         _opt;
    private readonly ILogger<FccmCaseIdPoller>  _log;

    public FccmCaseIdPoller(
        IServiceProvider                sp,
        IOptions<IntegrationOptions>    opt,
        ILogger<FccmCaseIdPoller>       log)
    {
        _sp  = sp;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("FccmCaseIdPoller starting. Interval = {Interval}, CaseIdTimeout = {Timeout}.",
                            _opt.PollingInterval, _opt.CaseIdTimeout);

        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope  = _sp.CreateScope();
                var       db     = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var       oracle = scope.ServiceProvider.GetRequiredService<FccmOracleReader>();
                var       kyc    = scope.ServiceProvider.GetRequiredService<IKycScreeningService>();

                await TickAsync(db, oracle, kyc, stop);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "FccmCaseIdPoller tick failed; will retry after interval.");
            }

            try
            {
                await Task.Delay(_opt.PollingInterval, stop);
            }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    internal async Task TickAsync(IApplicationDbContext db, FccmOracleReader oracle, IKycScreeningService kyc, CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var cutoff = now - _opt.CaseIdTimeout;

        // ---- Phase A — AwaitingCaseId rows ----
        var awaiting = await db.KycCaseRequests
            .Where(x => x.Status == KycCaseStatus.AwaitingCaseId && x.FccmRequestId != null)
            .OrderBy(x => x.SubmittedAt)
            .Take(50)                                              // soft batch cap per tick
            .ToListAsync(ct);

        // Verbose diagnostics — one line per tick summarising what the poller is
        // working on (helps confirm it is alive and which rows it is chasing).
        if (awaiting.Count > 0)
            _log.LogDebug("Poller tick: {Count} AwaitingCaseId row(s) -> [{Ids}]",
                awaiting.Count, string.Join(",", awaiting.Select(r => $"{r.Id}:req{r.FccmRequestId}")));

        foreach (var row in awaiting)
        {
            row.LastPolledAt = now;

            var caseId = await oracle.ReadCaseIdAsync(row.FccmRequestId!, ct);
            if (string.IsNullOrWhiteSpace(caseId) && row.SubmittedAt >= cutoff)
                _log.LogDebug("KycCaseRequest {Id} (req {ReqId}): still awaiting CASE_ID (age {Age}).",
                    row.Id, row.FccmRequestId, now - row.SubmittedAt);
            if (!string.IsNullOrWhiteSpace(caseId))
            {
                row.FccmCaseId    = caseId;
                row.Status        = KycCaseStatus.CaseCreated;
                row.CaseCreatedAt = now;
                row.LastUpdatedAt = now;
                row.FccmResponsePayload = FccmExchangeLog.Append(row.FccmResponsePayload, $"POLL CASE_ID -> {caseId} (CaseCreated)");
                _log.LogInformation("KycCaseRequest {Id}: CaseId provisioned ({CaseId}).", row.Id, caseId);

                // Parity with the trade path: stamp the resolved FCCM case id onto
                // the linked transaction row so the transaction detail / list / status
                // pill surface Fccm_Case_Id for KYC too (previously only the
                // KycCaseRequest row carried it). Only fills when empty — never
                // overwrites an existing case id.
                if (row.TransactionId is not null)
                {
                    var txId = (int)row.TransactionId.Value;
                    var txn = await db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == txId, ct);
                    if (txn is not null && string.IsNullOrWhiteSpace(txn.FccmCaseId))
                    {
                        txn.FccmCaseId     = caseId;
                        txn.LastUpdatedDate = now;
                        _log.LogInformation(
                            "KycCaseRequest {Id}: stamped Fccm_Case_Id={CaseId} onto transaction {TxId}.",
                            row.Id, caseId, txId);
                    }
                }
                continue;
            }

            if (row.SubmittedAt < cutoff)
            {
                row.Status        = KycCaseStatus.Timeout;
                row.ErrorDetail   = $"FCCM CASE_ID not provisioned within {_opt.CaseIdTimeout}.";
                row.LastUpdatedAt = now;
                row.FccmResponsePayload = FccmExchangeLog.Append(row.FccmResponsePayload, $"POLL CASE_ID -> null; TIMEOUT after {now - row.SubmittedAt} (no CASE_ID within {_opt.CaseIdTimeout})");
                _log.LogWarning("KycCaseRequest {Id}: timeout — no CaseId after {Elapsed}.", row.Id, now - row.SubmittedAt);
            }
        }
        if (awaiting.Count > 0) await db.SaveChangesAsync(ct);

        // ---- Phase B — CaseCreated rows awaiting risk score ----
        var caseCreated = await db.KycCaseRequests
            .Where(x => x.Status == KycCaseStatus.CaseCreated && x.FccmRequestId != null && x.RiskCategoryKey == null)
            .OrderBy(x => x.LastUpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var row in caseCreated)
        {
            row.LastPolledAt = now;

            var risk = await oracle.ReadRiskCategoryKeyAsync(row.FccmRequestId!, ct);
            if (!string.IsNullOrWhiteSpace(risk))
            {
                row.RiskCategoryKey = risk;
                row.Status          = KycCaseStatus.RiskAssessed;
                row.RiskAssessedAt  = now;
                row.LastUpdatedAt   = now;
                row.FccmResponsePayload = FccmExchangeLog.Append(row.FccmResponsePayload, $"POLL RISK -> {risk} (RiskAssessed)");
                _log.LogInformation("KycCaseRequest {Id}: risk key {Risk} captured; flow complete.", row.Id, risk);

                // Legacy parity: reconcile FCCM (TBML) vs BRAINS and write the
                // display band back to the customer snapshot + Cstm_Brains, then
                // push the FCCM-side write-backs (case priority + customer name).
                await ReconcileRiskAsync(db, oracle, kyc, row, risk!, now, _log, ct);
            }
        }
        if (caseCreated.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ports the legacy <c>caseInsertionController.UpdateRiskInRepo</c> +
    /// Cstm_Brains reconciliation. FCCM RISK_CATEGORY_KEY (1 Low / 2 Medium /
    /// 3 High) is compared against the BRAINS band (0 PEP / 1 High / 2 Medium /
    /// 3 Low) for the customer; the higher-risk one wins (legacy quirks
    /// preserved: BRAINS PEP=0 always wins). The reconciled display label —
    /// e.g. "High - TBML" — is written into the customer snapshot's
    /// UDF_Data.$.RiskScore (what the dashboard risk-mix reads) and upserted
    /// into dbo.Cstm_Brains keyed by Transaction_ID.
    /// </summary>
    /// <summary>
    /// Reconciles FCCM (TBML) vs BRAINS for a case row and persists the result
    /// (Cstm_Brains upsert + snapshot RiskScore + FCCM-side write-backs). Static
    /// + <see cref="ILogger"/>-parameterised so the on-demand operational actions
    /// (Re-poll / Re-fetch in <c>KycCaseOperationsService</c>) reuse the exact
    /// same logic as the background poller. Does NOT call SaveChanges — the caller
    /// commits (the poller batches per tick; the ops service saves per action).
    /// </summary>
    internal static async Task ReconcileRiskAsync(
        IApplicationDbContext db, FccmOracleReader oracle, IKycScreeningService kyc,
        KycCaseRequest row, string riskCategoryKey, DateTime now, ILogger log, CancellationToken ct)
    {
        if (row.TransactionId is null)
        {
            log.LogDebug("KycCaseRequest {Id}: no TransactionId; skipping Cstm_Brains reconciliation.", row.Id);
            return;
        }
        var txnId = (int)row.TransactionId.Value;

        // ---- FCCM (TBML) numeric: RISK_CATEGORY_KEY 1 Low / 2 Medium / 3 High (default Low). ----
        var fccmScore = int.TryParse(riskCategoryKey.Trim(), out var fk) && fk is 1 or 2 or 3 ? fk : 1;

        // ---- BRAINS: call the screening service for this customer. ----
        int? brainsScore = null;
        try
        {
            var brains = await kyc.GetKycForCustomerAsync(row.CustomerId, ct);
            brainsScore = BrainsBandToScore(brains?.RiskScore);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "KycCaseRequest {Id}: BRAINS screening failed; reconciling on FCCM only.", row.Id);
        }

        var (finalNum, _, display) = Reconcile(fccmScore, brainsScore);

        // ---- Upsert Cstm_Brains (one row per transaction; legacy varchar columns). ----
        var cstm = await db.CstmBrains.FirstOrDefaultAsync(x => x.TransactionId == txnId, ct);
        if (cstm is null)
        {
            cstm = new CstmBrains { TransactionId = txnId };
            db.CstmBrains.Add(cstm);
        }
        cstm.CustomerNo  = row.CustomerId;
        cstm.FccmScore   = fccmScore.ToString();
        cstm.BrainsScore = brainsScore?.ToString();
        cstm.FinalScore  = finalNum.ToString();
        cstm.Display     = display;

        // ---- Write the display band into the customer snapshot(s) for this txn. ----
        var snapshots = await db.TransactionCustomerSnapshots
            .Where(s => s.TransactionId == txnId)
            .ToListAsync(ct);

        foreach (var snap in snapshots)
            snap.UdfData = SetRiskScoreJson(snap.UdfData, display);

        log.LogInformation(
            "KycCaseRequest {Id}: reconciled risk txn {Txn} — FCCM={Fccm}, BRAINS={Brains} => {Display} (updated {Snaps} snapshot(s)).",
            row.Id, txnId, fccmScore, brainsScore?.ToString() ?? "(none)", display, snapshots.Count);

        // ---- Legacy FCCM-side write-backs (best-effort) ----
        // Ports the two Oracle writes legacy did alongside UpdateRiskInRepo:
        //   1. KDD_CASE_CUSTOMERS.FULL_NM synced from fcc_ob_cust (UpdateRiskInRepo
        //      first statement).
        //   2. KDD_CASES.PRIORITY_CD mapped from FCC_OB_RA.RISK_CATEGORY_KEY
        //      (UpdateCasePriority).
        // Best-effort: a failure here must NOT abort the reconciliation (the risk
        // has already been written to our side). Oracle is known reachable at this
        // point (ReadRiskCategoryKeyAsync just succeeded).
        var fccmReq = row.FccmRequestId;
        if (!string.IsNullOrWhiteSpace(fccmReq))
        {
            try
            {
                await oracle.SyncCaseCustomerNameAsync(fccmReq!, ct);
                await oracle.UpdateCasePriorityAsync(fccmReq!, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex,
                    "KycCaseRequest {Id}: FCCM-side write-back (FULL_NM / PRIORITY_CD) failed for req {ReqId}; risk reconciliation already persisted.",
                    row.Id, fccmReq);
            }
        }
    }

    /// <summary>BRAINS band label → its servlet numeric code (0 PEP / 1 High / 2 Medium / 3 Low).</summary>
    private static int? BrainsBandToScore(string? band) => band?.Trim().ToLowerInvariant() switch
    {
        "pep"    => 0,
        "high"   => 1,
        "medium" => 2,
        "low"    => 3,
        _        => null
    };

    /// <summary>
    /// Legacy Cstm_Brains Final_Score + Display computation. FCCM uses the
    /// 1/2/3 = Low/Medium/High scale; BRAINS uses 0/1/2/3 = PEP/High/Medium/Low.
    /// Rules (verbatim from legacy SQL):
    ///   • BRAINS = 0 (PEP)      → Final 0, suffix " - BRAINS"
    ///   • FCCM &gt; BRAINS       → Final = FCCM, suffix " - TBML"
    ///   • FCCM &lt; BRAINS       → Final = BRAINS, suffix " - BRAINS"
    ///   • equal                  → Final = FCCM, suffix " - BRAINS &amp; TBML SCORES ARE EQUAL"
    /// Final numeric → label: 1 Low / 2 Medium / (3 or 0) High.
    /// If BRAINS is unavailable, fall back to FCCM only (suffix " - TBML").
    /// </summary>
    public static (int finalNum, string finalLabel, string display) Reconcile(int fccmScore, int? brainsScore)
    {
        int finalNum;
        string suffix;

        if (brainsScore is null)
        {
            finalNum = fccmScore;
            suffix   = " - TBML";
        }
        else if (brainsScore.Value == 0)
        {
            finalNum = 0;
            suffix   = " - BRAINS";
        }
        else if (fccmScore > brainsScore.Value)
        {
            finalNum = fccmScore;
            suffix   = " - TBML";
        }
        else if (fccmScore < brainsScore.Value)
        {
            finalNum = brainsScore.Value;
            suffix   = " - BRAINS";
        }
        else
        {
            finalNum = fccmScore;
            suffix   = " - BRAINS & TBML SCORES ARE EQUAL";
        }

        var label = finalNum switch
        {
            1 => "Low",
            2 => "Medium",
            _ => "High"   // 3 or 0 (PEP) → High, per legacy
        };

        return (finalNum, label, label + suffix);
    }

    /// <summary>Set (or add) the $.RiskScore property on a UDF_Data JSON blob, preserving other keys.</summary>
    public static string SetRiskScoreJson(string? udfData, string riskScore)
    {
        JsonObject obj;
        try
        {
            obj = string.IsNullOrWhiteSpace(udfData)
                ? new JsonObject()
                : JsonNode.Parse(udfData) as JsonObject ?? new JsonObject();
        }
        catch
        {
            obj = new JsonObject();
        }

        obj["RiskScore"] = riskScore;
        return obj.ToJsonString();
    }
}
