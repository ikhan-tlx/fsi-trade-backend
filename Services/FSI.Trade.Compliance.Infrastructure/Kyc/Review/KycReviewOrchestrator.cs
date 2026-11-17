using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Application.Features.Transactions.Create;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

/// <summary>
/// Creates the review KYC transaction for a customer and hands it to the person
/// who started the review.
///
/// 2026-11 rework: this used to impersonate a configured service "PERIODIC
/// REVIEWER" and auto-drive the whole workflow (SendToAuthorizer → Authorize →
/// FCCM) in the background, bypassing the normal maker/checker flow. It now:
///   1. clones the last terminal KYC + copies the product data AS THE INITIATOR
///      (the signed-in user who clicked "Start review"), so they own the Draft;
///   2. stamps origin = KYC Review (TmX_Lookup TRANSACTION_ORIGIN) so the grid /
///      detail can badge "Initiated by KYC review";
///   3. STOPS — it does NOT advance the workflow. The initiator opens the new
///      KYC transaction and runs the normal workflow (Send to Authorizer → …).
/// The create-gate still Blocks if a KYC is already active for the customer.
/// </summary>
public class KycReviewOrchestrator : IKycReviewOrchestrator
{
    private readonly IMediator                _mediator;
    private readonly IWorkflowEngine          _engine;      // retained for DI parity
    private readonly IKycTransactionCloner    _cloner;
    private readonly IAmbientUserContext      _ambient;     // retained for DI parity
    private readonly IApplicationDbContext    _db;
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt; // retained for DI parity
    private readonly ILogger<KycReviewOrchestrator> _log;

    public KycReviewOrchestrator(
        IMediator mediator, IWorkflowEngine engine, IKycTransactionCloner cloner,
        IAmbientUserContext ambient, IApplicationDbContext db,
        IOptionsMonitor<PeriodicReviewOptions> opt, ILogger<KycReviewOrchestrator> log)
    {
        _mediator = mediator; _engine = engine; _cloner = cloner; _ambient = ambient;
        _db = db; _opt = opt; _log = log;
    }

    public async Task<ReviewRunOutcome> RunAsync(string customerCode, int sourceTransactionId, CancellationToken ct = default)
    {
        _log.LogInformation(
            "[KYC-REVIEW] START customer={Cust} sourceTxn={Src} — creating a draft review KYC for the initiator (no auto-drive).",
            customerCode, sourceTransactionId);

        // 1. Build + create the clone (header + customer snapshot) AS THE
        //    INITIATOR (no impersonation): the CreateTransactionCommand handler
        //    stamps the current ambient user as the maker, so the person who
        //    started the review owns the Draft and can drive the workflow.
        CreateTransactionCommand createCmd;
        try
        {
            createCmd = await _cloner.BuildCreateCommandAsync(sourceTransactionId, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KYC review: clone build failed for source txn {Src}.", sourceTransactionId);
            return new ReviewRunOutcome(ReviewOutcome.Failed, null, null, "Clone build failed: " + ex.Message);
        }

        int newTxnId; Guid? processId;
        try
        {
            var created = await _mediator.Send(createCmd, ct);
            newTxnId  = created.transactionId;
            processId = created.processInstanceId;
        }
        catch (ConflictException ex) when (ex.Code == "kyc_active_exists")
        {
            _log.LogInformation("KYC review blocked for customer {Cust}: {Msg}", customerCode, ex.Message);
            return new ReviewRunOutcome(ReviewOutcome.Blocked, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KYC review: create failed for customer {Cust}.", customerCode);
            return new ReviewRunOutcome(ReviewOutcome.Failed, null, null, "Create failed: " + ex.Message);
        }

        if (processId is null || processId == Guid.Empty)
            return new ReviewRunOutcome(ReviewOutcome.Failed, newTxnId, null,
                "New transaction has no workflow process attached.");

        // 2. Copy the product payload (detail UDF + grids + snapshot udf) from source.
        try
        {
            await _cloner.CopyProductDataAsync(sourceTransactionId, newTxnId, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KYC review: copy product data failed src {Src} -> new {New}.", sourceTransactionId, newTxnId);
            return new ReviewRunOutcome(ReviewOutcome.Failed, newTxnId, null, "Copy failed: " + ex.Message);
        }

        // 3. Stamp origin = KYC Review + read back the transaction number for the
        //    FE redirect. NO workflow auto-drive — the initiator takes it from here.
        string? txnNumber = null;
        try
        {
            var originId = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "TRANSACTION_ORIGIN" && l.HiddenValue == "KYC_REVIEW")
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(ct);

            var txn = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == newTxnId, ct);
            if (txn is not null)
            {
                txnNumber = txn.TransactionNumber;
                if (originId is not null)
                {
                    txn.TransactionOriginLkp = originId;
                    await _db.SaveChangesAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            // Origin tagging is best-effort; the review txn is already created.
            _log.LogWarning(ex, "KYC review: could not stamp origin / read number for new txn {New}.", newTxnId);
        }

        _log.LogInformation(
            "[KYC-REVIEW] COMPLETE customer={Cust}: created draft review KYC txn {New} ({Num}) for the initiator.",
            customerCode, newTxnId, txnNumber);

        return new ReviewRunOutcome(ReviewOutcome.Completed, newTxnId, null,
            "KYC review draft created — open it to review and send to the authorizer.")
        {
            NewTransactionNumber = txnNumber,
        };
    }
}
