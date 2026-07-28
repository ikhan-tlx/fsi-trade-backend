using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Logging;
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

        using var wf = WorkflowLog.Begin(_logger, "FCCM Trade Case", ("TransactionId", transactionId));
        wf.Step("Trade received — building FCCM case from staging.");

        // 1. Load main DB data ------------------------------------------------
        wf.Step("Loading transaction, customer snapshot, detail and ticked flags.");
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
            select new { c.FlagCode, c.FlagDescription, tf.Source, tf.MatchedValue })
            .ToListAsync(ct);

        var redflagsCount = tickedFlags.Count;
        wf.Step($"Loaded: customer='{customer?.CustomerName ?? "(none)"}', flags ticked={redflagsCount}.");

        // 2. Pre-staging Oracle setup -----------------------------------------
        wf.Step("Pre-staging Oracle setup (truncate RT_CASE_TRACKING + EVENT_STAGING, reserve MessageId).");
        var messageId = await wf.ExternalAsync(
            "Oracle pre-staging (truncate + MESSAGE_ID_SEQ + RT_CASE_TRACKING)", async () =>
        {
            await _oracle.TruncateRtCaseTrackingAsync(ct);
            await _oracle.TruncateEventStagingAsync(ct);
            var mid = await _oracle.GetNextMessageIdAsync(ct);
            await _oracle.InsertRtCaseTrackingAsync(mid, ct);
            return mid;
        });
        wf.Detail($"Reserved MessageId={messageId}.");

        // 3. Staging row builds + INSERTs -------------------------------------
        wf.Step("Building + inserting staging rows (LC_TRN_DET, CUST_MASTER, BENEFICIARY_DETAIL, EVENT_STAGING).");
        var productCode  = ExtractProductCodeFromUdf(detail?.UdfData) ?? $"PID-{tx.ProductId}";
        var customerName = customer?.CustomerName ?? "";
        var clientRef    = tx.ClientReferenceNumber ?? "";

        var lcTrnDet    = BuildLcTrnDetStaging(messageId, productCode, tx.TransactionNumber ?? tx.ClientReferenceNumber, detail);
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

        // N flag event rows — catalogue-driven, NO hardcoded indicator text.
        // Auto rows append the matched value (e.g. the sanctioned country/good) so
        // a per-match flag emits one event NAMING each hit, mirroring legacy
        // PopulateRedflags_Event_Staging which logged the specific country/good.
        foreach (var f in tickedFlags)
        {
            var description = f.Source == "Auto" && !string.IsNullOrWhiteSpace(f.MatchedValue)
                ? $"{f.FlagDescription} [{f.MatchedValue}]"
                : f.FlagDescription;
            await _oracle.InsertEventStagingAsync(new EventStaging
            {
                MsgId       = messageId,
                Title       = clientRef,
                InputMatch  = "Customer",
                // Reflect the flag's origin instead of a hardcoded label: auto-flags read
                // "Auto Redflag", everything else (manual red flags) reads "Manual Redflag".
                InputName   = f.Source == "Auto" ? "Auto Redflag" : "Manual Redflag",
                Description = description,
                TsDate      = now,
            }, ct);
        }

        wf.Success($"Staging records saved — MsgId={messageId}: 1 LcTrnDet + 1 CustMaster + 1 Benef + " +
                   $"{1 + redflagsCount} events (1 customer + {redflagsCount} flags).");

        // 4. Branch on redflagsCount ------------------------------------------
        if (redflagsCount == 0)
        {
            // Legacy no-flag branch — no FCCM contact. Local status flip
            // + sentinel return. Don't even call the SP.
            tx.TransactionStatusLkp = NoFccmHitStatusLkp;
            tx.FccmCaseId           = NoFccmHitSentinel;
            tx.LastUpdatedDate      = now;
            await _db.SaveChangesAsync(ct);

            wf.Step($"Zero flags — FCCM not contacted; local status flipped to {NoFccmHitStatusLkp} (No FCCM Hit).");
            wf.EndSuccess($"TxId={transactionId} Case={NoFccmHitSentinel} (no-hit) Flags=0.");

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
        wf.Step("Executing InsertTransFromRepoAsync (TMX_TRANS_FROM_REPO correlation insert).");
        await wf.ExternalAsync(
            $"TMX_TRANS_FROM_REPO insert (Customer='{customerName}', ClientRef='{clientRef}', TxId={transactionId}, TxNo={tx.TransactionNumber})",
            () => _oracle.InsertTransFromRepoAsync(
                customerName:          customerName,
                clientReferenceNumber: clientRef,
                transactionId:         transactionId,
                transactionNumber:     tx.TransactionNumber,
                ct:                    ct));

        // 4b. Stored procedure — builds the FCCM case from staging
        wf.Step("Executing stored procedure PR_CREATE_EVENT_DEMO.");
        string? caseId;
        try
        {
            caseId = await wf.ExternalAsync(
                $"PR_CREATE_EVENT_DEMO(MessageId={messageId}, Product='{FccmSpProductLiteral}')", () =>
                _oracle.ExecuteCreateEventDemoProcAsync(messageId, FccmSpProductLiteral, ct));
        }
        catch (Exception ex)
        {
            // The [CALL] … FAILED line above already carries the SQL error + stack.
            wf.EndFailure("PR_CREATE_EVENT_DEMO", ex.Message);
            throw;
        }

        if (string.IsNullOrWhiteSpace(caseId))
        {
            wf.Failed("Stored procedure execution", $"PR_CREATE_EVENT_DEMO returned no case id (MessageId={messageId}).");
            wf.EndFailure("PR_CREATE_EVENT_DEMO", "Stored procedure returned a null/empty case id.");
            throw new InvalidOperationException(
                $"PR_CREATE_EVENT_DEMO returned null case id for TxId={transactionId} (MessageId={messageId}).");
        }
        var createdCaseId = caseId!;
        wf.Success($"Case created — CaseId={createdCaseId}.");

        // 4c. Post-SP enrichment
        wf.Step("Post-SP enrichment (KDD_CASE_CUSTOMERS insert + KDD_CASES title update).");
        await wf.ExternalAsync("InsertKddCaseCustomersAsync + UpdateKddCasesTitleAsync", async () =>
        {
            await _oracle.InsertKddCaseCustomersAsync(
                customerName:      customerName,
                clientReferenceId: clientRef,
                transactionId:     transactionId,
                caseId:            createdCaseId,
                ct:                ct);
            await _oracle.UpdateKddCasesTitleAsync(
                caseId:            createdCaseId,
                transactionNumber: tx.TransactionNumber ?? "",
                ct:                ct);
        });

        // 4d. Persist case id back on the transaction
        tx.FccmCaseId      = createdCaseId;
        tx.LastUpdatedDate = now;
        await _db.SaveChangesAsync(ct);
        wf.Step("Persisted Fccm_Case_Id on the transaction.");

        wf.EndSuccess($"TxId={transactionId} Case={createdCaseId} MsgId={messageId} FlagEvents={redflagsCount}.");

        return new FccmTradeSubmissionResult
        {
            TransactionId  = transactionId,
            FccmCaseId     = createdCaseId,
            WasSkipped     = false,
            FlagEventCount = redflagsCount,
            SubmittedAt    = now
        };
    }

    // ---- Staging row builders --------------------------------------------

    private static LcTrnDetStaging BuildLcTrnDetStaging(
        string messageId,
        string productCode,
        string? contractId,
        Domain.Entities.Transaction.TransactionDetail? detail)
    {
        var udf = ParseUdf(detail?.UdfData);

        return new LcTrnDetStaging
        {
            MessageId        = messageId,
            Prouct           = productCode,                        // legacy typo preserved
            // Contract internal id. PR_CREATE_EVENT_DEMO's 'FT' branch inserts
            // this (FTID) into KDD_CASE_TRD_FIN_CNTRCT_EVT.TRD_FIN_CNTRCT_INTRL_ID,
            // which is NOT NULL — leaving it null caused ORA-01400. Use the
            // transaction number (business contract ref) with messageId fallback.
            Ftid             = string.IsNullOrWhiteSpace(contractId) ? messageId : contractId,
            Lcid             = UdfString(udf, "LCId"),
            Lcamount         = UdfDecimal(udf, "LCAmount"),
            // GOOD_SRVC_CD (NOT NULL in KDD_CASE_TRD_FIN_GOOD_SRVC) is sourced
            // from HSCODE by PR_CREATE_EVENT_DEMO — fall back to 'NA' when the
            // UDF has no HS code so the proc's goods insert doesn't hit ORA-01400.
            Hscode           = UdfString(udf, "HSCode") ?? "NA",
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
            // CNTRCT_EVENT_NB (NOT NULL) is sourced from VERSION by
            // PR_CREATE_EVENT_DEMO — default to 1 when absent from the UDF.
            Version          = UdfDecimal(udf, "Version") ?? 1m,
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
