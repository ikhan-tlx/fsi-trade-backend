using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.FccmStaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// Slice 9 — Trade-to-FCCM submission. Replicates the legacy
/// <c>ICBC_Integration_Test.TradeController.TradeTransactionToFCCM</c>
/// pipeline using the new domain model and the new flag catalogue.
///
/// Pipeline order — see <c>docs/LEGACY_FCCM_TRADE_FLOW.md</c> for the
/// full op-by-op rationale:
///
///   1. Load transaction + customer snapshot + beneficiaries +
///      ticked flags from the main SQL Server DB.
///   2. Pre-staging Oracle setup:
///        a. TRUNCATE RT_CASE_TRACKING + EVENT_STAGING
///        b. SELECT MESSAGE_ID_SEQ.nextval → MessageId
///        c. INSERT INTO RT_CASE_TRACKING (MessageId)
///   3. Build + INSERT staging rows on Oracle (MANTAS schema):
///        • BENEFICIARY_DETAIL_STAGING (1 row)
///        • CUST_MASTER_STAGING        (1 row)
///        • LC_TRN_DET_STAGING         (1 row, trade fields only)
///        • EVENT_STAGING              (1 customer + N flag events,
///                                      sourced from TmX_Transaction_Flag
///                                      ⨝ TmX_Flag_Catalogue.Flag_Description
///                                      — single source of truth)
///   4. Branch on redflagsCount:
///        • > 0 → INSERT TMX_TRANS_FROM_REPO + EXEC PR_CREATE_EVENT_DEMO
///                + INSERT KDD_CASE_CUSTOMERS + UPDATE KDD_CASES.CASE_TITL_NM
///                + persist Fccm_Case_Id on the transaction
///        • = 0 → no FCCM contact. Local Transaction_Status_Lkp flips to
///                3059 ("No FCCM Hit" — legacy hardcoded constant) and
///                Fccm_Case_Id stays NULL. Returns sentinel "CA-NOHIT".
///
/// Idempotency: <see cref="SubmitAsync"/> short-circuits when
/// <c>Fccm_Case_Id</c> is already populated; <see cref="ResubmitAsync"/>
/// clears it first and re-runs.
/// </summary>
internal class FccmTradeCaseService : IFccmTradeCaseService
{
    /// <summary>
    /// Legacy hardcoded lookup id for the "No FCCM Hit" transaction
    /// status — written to <c>TmX_Transaction.Transaction_Status_Lkp</c>
    /// when the submit-time flag count is zero. From
    /// <c>TradeController.cs:740</c>.
    /// </summary>
    private const int NoFccmHitStatusLkp = 3059;

    /// <summary>
    /// Sentinel case-id returned to the caller when the no-flag branch
    /// fires (no Oracle SP invocation, no FCCM case created). Persisted
    /// onto <c>Fccm_Case_Id</c> so callers can distinguish "never
    /// submitted" (NULL) from "submitted but FCCM ruled out a case"
    /// (this sentinel). From <c>TradeController.cs:752</c>.
    /// </summary>
    private const string NoFccmHitSentinel = "CA-NOHIT";

    /// <summary>
    /// Hardcoded literal the legacy passes as the SP's Product
    /// parameter (NOT the transaction's real product code).
    /// From <c>TradeController.cs:729</c>.
    /// </summary>
    private const string FccmSpProductLiteral = "FT";

    private readonly IApplicationDbContext _db;
    private readonly FccmOracleReader      _oracle;
    private readonly ILogger<FccmTradeCaseService> _logger;

    public FccmTradeCaseService(
        IApplicationDbContext db,
        FccmOracleReader      oracle,
        ILogger<FccmTradeCaseService> logger)
    {
        _db     = db;
        _oracle = oracle;
        _logger = logger;
    }

    public async Task<FccmTradeSubmissionResult> SubmitAsync(int transactionId, CancellationToken ct = default)
    {
        var tx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct)
            ?? throw new NotFoundException("transaction_not_found",
                $"Transaction {transactionId} does not exist.");

        if (!string.IsNullOrWhiteSpace(tx.FccmCaseId))
        {
            _logger.LogInformation(
                "FccmTradeCaseService.SubmitAsync: transaction {TxId} already has Fccm_Case_Id={CaseId} — skipping.",
                transactionId, tx.FccmCaseId);

            return new FccmTradeSubmissionResult
            {
                TransactionId  = transactionId,
                FccmCaseId     = tx.FccmCaseId,
                WasSkipped     = true,
                FlagEventCount = 0,
                SubmittedAt    = DateTime.UtcNow
            };
        }

        return await SubmitInternalAsync(transactionId, ct);
    }

    public async Task<FccmTradeSubmissionResult> ResubmitAsync(int transactionId, CancellationToken ct = default)
    {
        var tx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct)
            ?? throw new NotFoundException("transaction_not_found",
                $"Transaction {transactionId} does not exist.");

        _logger.LogWarning(
            "FccmTradeCaseService.ResubmitAsync: transaction {TxId} resubmit requested. Prior Fccm_Case_Id={CaseId} will be cleared.",
            transactionId, tx.FccmCaseId);

        tx.FccmCaseId      = null;
        tx.LastUpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await SubmitInternalAsync(transactionId, ct);
    }

    // ---- Pipeline ---------------------------------------------------------

    private async Task<FccmTradeSubmissionResult> SubmitInternalAsync(int transactionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // 1. Load main DB data ------------------------------------------------
        var tx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct)
            ?? throw new NotFoundException("transaction_not_found",
                $"Transaction {transactionId} does not exist.");

        var detail = await _db.TransactionDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TransactionId == transactionId, ct);

        var customer = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransactionId == transactionId, ct);

        // Flag projection — the Slice 8 catalogue win. One LINQ query,
        // one foreach, Flag_Description is the canonical text.
        var tickedFlags = await (
            from tf in _db.TransactionFlags.AsNoTracking()
            where tf.TransactionId == transactionId && tf.IsFlagged
            join c in _db.FlagCatalogues.AsNoTracking() on tf.FlagId equals c.FlagId
            where c.ActiveFlag
            select new { c.FlagCode, c.FlagDescription })
            .ToListAsync(ct);

        var redflagsCount = tickedFlags.Count;

        // 2. Pre-staging Oracle setup -----------------------------------------
        await _oracle.TruncateRtCaseTrackingAsync(ct);
        await _oracle.TruncateEventStagingAsync(ct);
        var messageId = await _oracle.GetNextMessageIdAsync(ct);
        await _oracle.InsertRtCaseTrackingAsync(messageId, ct);

        // 3. Staging row builds + INSERTs -------------------------------------
        var productCode  = ExtractProductCodeFromUdf(detail?.UdfData) ?? $"PID-{tx.ProductId}";
        var customerName = customer?.CustomerName ?? "";
        var clientRef    = tx.ClientReferenceNumber ?? "";

        var lcTrnDet    = BuildLcTrnDetStaging(messageId, productCode, detail);
        var custMaster  = BuildCustMasterStaging(messageId, productCode, customer, now);
        var benefDetail = BuildBeneficiaryDetailStaging(messageId, productCode, tx);

        await _oracle.InsertLcTrnDetStagingAsync(lcTrnDet, ct);
        await _oracle.InsertCustMasterStagingAsync(custMaster, ct);
        await _oracle.InsertBeneficiaryDetailStagingAsync(benefDetail, ct);

        // Customer-info event row
        await _oracle.InsertEventStagingAsync(new EventStaging
        {
            MsgId       = messageId,
            Title       = clientRef,
            InputMatch  = "Customer",
            InputName   = customerName,
            Description = $"Customer Details: {customerName}",
            TsDate      = now,
        }, ct);

        // N flag event rows — catalogue-driven, NO hardcoded indicator text
        foreach (var f in tickedFlags)
        {
            await _oracle.InsertEventStagingAsync(new EventStaging
            {
                MsgId       = messageId,
                Title       = clientRef,
                InputMatch  = "Customer",
                InputName   = "Manual Redflag",
                Description = f.FlagDescription,
                TsDate      = now,
            }, ct);
        }

        _logger.LogInformation(
            "FccmTradeCaseService: staging rows written for TxId={TxId} MsgId={MsgId} — 1 LcTrnDet + 1 CustMaster + 1 Benef + {EventCount} events (1 customer + {FlagCount} flags).",
            transactionId, messageId, 1 + redflagsCount, redflagsCount);

        // 4. Branch on redflagsCount ------------------------------------------
        if (redflagsCount == 0)
        {
            // Legacy no-flag branch — no FCCM contact. Local status flip
            // + sentinel return. Don't even call the SP.
            tx.TransactionStatusLkp = NoFccmHitStatusLkp;
            tx.FccmCaseId           = NoFccmHitSentinel;
            tx.LastUpdatedDate      = now;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "FccmTradeCaseService: TxId={TxId} submitted with ZERO flags — FCCM not contacted, status flipped to {StatusLkp}.",
                transactionId, NoFccmHitStatusLkp);

            return new FccmTradeSubmissionResult
            {
                TransactionId  = transactionId,
                FccmCaseId     = NoFccmHitSentinel,
                WasSkipped     = false,
                FlagEventCount = 0,
                SubmittedAt    = now
            };
        }

        // 4a. Correlation insert (Oracle TMX_TRANS_FROM_REPO)
        await _oracle.InsertTransFromRepoAsync(
            customerName:           customerName,
            clientReferenceNumber:  clientRef,
            transactionId:          transactionId,
            transactionNumber:      tx.TransactionNumber,
            ct:                     ct);

        // 4b. Stored procedure — builds the FCCM case from staging
        var caseId = await _oracle.ExecuteCreateEventDemoProcAsync(messageId, FccmSpProductLiteral, ct)
                     ?? throw new InvalidOperationException(
                         $"PR_CREATE_EVENT_DEMO returned null case id for TxId={transactionId} (MessageId={messageId}).");

        // 4c. Post-SP enrichment
        await _oracle.InsertKddCaseCustomersAsync(
            customerName:       customerName,
            clientReferenceId:  clientRef,
            transactionId:      transactionId,
            caseId:             caseId,
            ct:                 ct);

        await _oracle.UpdateKddCasesTitleAsync(
            caseId:             caseId,
            transactionNumber:  tx.TransactionNumber ?? "",
            ct:                 ct);

        // 4d. Persist case id back on the transaction
        tx.FccmCaseId      = caseId;
        tx.LastUpdatedDate = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "FccmTradeCaseService: TxId={TxId} submitted to FCCM. CaseId={CaseId} MessageId={MsgId} FlagEvents={FlagCount}.",
            transactionId, caseId, messageId, redflagsCount);

        return new FccmTradeSubmissionResult
        {
            TransactionId  = transactionId,
            FccmCaseId     = caseId,
            WasSkipped     = false,
            FlagEventCount = redflagsCount,
            SubmittedAt    = now
        };
    }

    // ---- Staging row builders --------------------------------------------

    private static LcTrnDetStaging BuildLcTrnDetStaging(
        string messageId,
        string productCode,
        Domain.Entities.Transaction.TransactionDetail? detail)
    {
        var udf = ParseUdf(detail?.UdfData);

        return new LcTrnDetStaging
        {
            MessageId        = messageId,
            Prouct           = productCode,                        // legacy typo preserved
            Lcid             = UdfString(udf, "LCId"),
            Lcamount         = UdfDecimal(udf, "LCAmount"),
            Hscode           = UdfString(udf, "HSCode"),
            Hscodeofgoods    = UdfString(udf, "HSCodeOfGoods"),
            Country          = UdfString(udf, "COCode") ?? UdfString(udf, "Country"),
            Portofloading    = UdfString(udf, "PortOfLoading"),
            Portofdischarge  = UdfString(udf, "PortOfDischarge"),
            Finaldest        = UdfString(udf, "FinalDestination"),
            Finaldestination = UdfString(udf, "FinalDestination"),
            Commodity        = UdfString(udf, "Commodity"),
            Goodsdescription = UdfString(udf, "GoodsDescription"),
            Descgoods        = UdfString(udf, "GoodsDescription"),
            Transportation   = UdfString(udf, "Transportation"),
            Shipdespatch     = UdfString(udf, "ShipDespatch"),
            Otherdespatchdet = UdfString(udf, "OtherDespatchDet"),
            Expiryplace      = UdfString(udf, "ExpiryPlace"),
            Additionalcond   = UdfString(udf, "AdditionalCond"),
            Instructions     = UdfString(udf, "Instructions"),
            Quantity         = UdfDecimal(udf, "Quantity"),
            Unitcurrency     = UdfString(udf, "UnitCurrency"),
            Flrunitprice     = UdfDecimal(udf, "FlrUnitPrice"),
            Capunitprice     = UdfDecimal(udf, "CapUnitPrice"),
            Sellunitprice    = UdfDecimal(udf, "SellUnitPrice"),
            Eifamnt          = UdfDecimal(udf, "EifAmnt"),
            Hsquant          = UdfDecimal(udf, "HSQuant"),
            Hsunit           = UdfString(udf, "HSUnit"),
            Version          = UdfDecimal(udf, "Version"),
            Lremarks         = UdfString(udf, "Remarks"),
            // Identifiers left null — per-product UDF mapping firms up later.
        };
    }

    private static CustMasterStaging BuildCustMasterStaging(
        string messageId,
        string productCode,
        Domain.Entities.Customer.TransactionCustomerSnapshot? customer,
        DateTime now)
    {
        return new CustMasterStaging
        {
            MessageId  = messageId,
            Product    = productCode,
            Customer   = customer?.CustomerName,
            Customerid = customer?.CustomerCode,
            DumpDt     = now,
        };
    }

    private static BeneficiaryDetailStaging BuildBeneficiaryDetailStaging(
        string messageId,
        string productCode,
        Domain.Entities.Transaction.Transaction tx)
    {
        return new BeneficiaryDetailStaging
        {
            MessageId         = messageId,
            Product           = productCode,
            Transactionnumber = tx.TransactionNumber,
        };
    }

    // ---- UDF JSON helpers ------------------------------------------------

    private static string? ExtractProductCodeFromUdf(string? udfData)
    {
        var doc = ParseUdf(udfData);
        return UdfString(doc, "CategoryCode");
    }

    private static JsonElement? ParseUdf(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? UdfString(JsonElement? root, string propertyName)
    {
        if (root is null) return null;
        if (!root.Value.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True   => "1",
            JsonValueKind.False  => "0",
            _                    => null,
        };
    }

    private static decimal? UdfDecimal(JsonElement? root, string propertyName)
    {
        if (root is null) return null;
        if (!root.Value.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDecimal(out var d) ? d : (decimal?)null,
            JsonValueKind.String => decimal.TryParse(prop.GetString(), out var d) ? d : (decimal?)null,
            _                    => null,
        };
    }
}
