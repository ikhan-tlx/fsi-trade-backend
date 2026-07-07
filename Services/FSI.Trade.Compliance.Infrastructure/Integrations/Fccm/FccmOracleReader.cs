using System.Data;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Domain.Entities.FccmStaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// Pure ADO.NET gateway for every FCCM Oracle operation in the new
/// backend. Replaces the legacy hybrid pattern (EF for staging tables,
/// raw ADO.NET for everything else) with a single consistent
/// <c>OracleConnection</c>-per-call approach.
///
/// Coverage (see <c>docs/LEGACY_FCCM_TRADE_FLOW.md</c>):
/// <list type="bullet">
///   <item>Slice 4 reads: <c>FCC_OB_RA</c> case-id polling +
///         risk-category lookup.</item>
///   <item>Slice 9 setup: TRUNCATE <c>RT_CASE_TRACKING</c> +
///         <c>EVENT_STAGING</c>, sequence read, RT_CASE_TRACKING
///         insert.</item>
///   <item>Slice 9 staging: parameterised INSERTs into the four
///         <c>MANTAS</c>-schema staging tables (1 per call,
///         <c>InsertEventStagingAsync</c> called N+1 times per submit).</item>
///   <item>Slice 9 case create: SP call
///         <c>PR_CREATE_EVENT_DEMO(MessageId, Product, NUM, RESID OUT)</c>.</item>
///   <item>Slice 9 enrichment: <c>KDD_CASE_CUSTOMERS</c> insert,
///         <c>KDD_CASES.CASE_TITL_NM</c> update,
///         <c>TMX_TRANS_FROM_REPO</c> correlation insert.</item>
/// </list>
///
/// Stub mode: when <c>Integration:FccmOracleConnection</c> is empty,
/// every method logs a warning and returns synthetic values (e.g.
/// <c>"STUB-{guid}"</c> for the SP, <c>"STUBSEQ-{guid}"</c> for the
/// sequence). The whole pipeline still exercises end-to-end in dev.
///
/// Connection lifecycle: every public method opens its own
/// <c>OracleConnection</c> with <c>using</c>. Connection pooling on
/// <c>Oracle.ManagedDataAccess.Core</c> handles re-use under the hood
/// — typical cost is a pool checkout + checkin, not a TCP handshake.
/// </summary>
public class FccmOracleReader
{
    private readonly IntegrationOptions        _opt;
    private readonly ILogger<FccmOracleReader> _log;
    private          bool                     _warnedAboutStub;

    public FccmOracleReader(IOptions<IntegrationOptions> opt, ILogger<FccmOracleReader> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    private bool IsStub => string.IsNullOrWhiteSpace(_opt.FccmOracleConnection);

    private OracleConnection CreateConnection() =>
        new OracleConnection(_opt.FccmOracleConnection);

    /// <summary>
    /// Opens the connection and pins the session's CURRENT_SCHEMA to the
    /// configured FCCM schema (default "MANTAS"). The integration references
    /// most FCCM objects UNQUALIFIED (RT_CASE_TRACKING, EVENT_STAGING,
    /// MESSAGE_ID_SEQ, FCC_OB_RA, PR_CREATE_EVENT_DEMO, KDD_CASES,
    /// KDD_CASE_CUSTOMERS, TMX_TRANS_FROM_REPO), so they resolve against the
    /// login user's schema. Legacy connected AS the schema owner ("mantas");
    /// we instead set CURRENT_SCHEMA so the login user can differ (e.g.
    /// ofsaafccm) while names still resolve to MANTAS. The login user still
    /// needs object privileges (grants) on the MANTAS objects. The schema name
    /// is a trusted config identifier; we sanitise it to a bare identifier as a
    /// defence-in-depth measure since it's interpolated into the ALTER SESSION.
    /// </summary>
    private async Task OpenAndPrepareAsync(OracleConnection con, CancellationToken ct)
    {
        await con.OpenAsync(ct);

        var schema = (_opt.FccmOracleSchema ?? "").Trim();
        if (schema.Length == 0) return;                                  // no override → use login default schema
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[A-Za-z][A-Za-z0-9_$#]*$"))
        {
            _log.LogWarning("FccmOracleSchema '{Schema}' is not a valid Oracle identifier — skipping CURRENT_SCHEMA override.", schema);
            return;
        }

        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"ALTER SESSION SET CURRENT_SCHEMA = {schema}";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =====================================================================
    // Slice 4 — KYC case poller (was previously stubbed, now real)
    // =====================================================================

    /// <summary>
    /// <c>SELECT CASE_ID FROM FCC_OB_RA WHERE REQUEST_ID = :p_req</c>.
    /// Returns null if no row yet (poller's "not yet" signal).
    /// </summary>
    public async Task<string?> ReadCaseIdAsync(string fccmRequestId, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return null; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = "SELECT CASE_ID FROM FCC_OB_RA WHERE REQUEST_ID = :p_req";
        cmd.Parameters.Add(":p_req", OracleDbType.Varchar2).Value = fccmRequestId;
        await OpenAndPrepareAsync(con, ct);

        var result = await cmd.ExecuteScalarAsync(ct);
        var caseId = result is null or DBNull ? null : result.ToString();
        // Verbose diagnostics — the exact FCC_OB_RA poll result, so a stuck/timing
        // case-id can be investigated (request id queried + whether a row/CASE_ID
        // exists yet upstream).
        _log.LogDebug("FCC_OB_RA poll: REQUEST_ID={RequestId} -> {Result}",
            fccmRequestId, caseId ?? "(no CASE_ID yet)");
        return caseId;
    }

    /// <summary>
    /// <c>SELECT RISK_CATEGORY_KEY FROM FCC_OB_RA WHERE REQUEST_ID = :p_req</c>.
    /// </summary>
    public async Task<string?> ReadRiskCategoryKeyAsync(string fccmRequestId, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return null; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = "SELECT RISK_CATEGORY_KEY FROM FCC_OB_RA WHERE REQUEST_ID = :p_req";
        cmd.Parameters.Add(":p_req", OracleDbType.Varchar2).Value = fccmRequestId;
        await OpenAndPrepareAsync(con, ct);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : result.ToString();
    }

    /// <summary>
    /// Legacy <c>caseInsertionController.UpdateCasePriority</c> — pushes the FCCM
    /// onboarding risk band back onto the KDD case as a priority code. Mirrors the
    /// legacy statement (bound parameter instead of string interpolation):
    /// <code>
    /// UPDATE KDD_CASES kc SET kc.PRIORITY_CD =
    ///   (SELECT CASE WHEN a.RISK_CATEGORY_KEY = 1 THEN 'Low'
    ///                WHEN a.RISK_CATEGORY_KEY = 2 THEN 'Medium'
    ///                WHEN a.RISK_CATEGORY_KEY = 3 THEN 'High' END
    ///      FROM FCC_OB_RA a INNER JOIN KDD_CASES b ON a.CASE_ID = b.CASE_INTRL_ID
    ///     WHERE a.REQUEST_ID = :p_req)
    ///  WHERE kc.CASE_INTRL_ID = (SELECT CASE_ID FROM FCC_OB_RA WHERE REQUEST_ID = :p_req)
    /// </code>
    /// Note (legacy quirk preserved): only 1/2/3 map; any other RISK_CATEGORY_KEY
    /// (e.g. 4) yields a NULL PRIORITY_CD, exactly as the legacy CASE did.
    /// No-op in stub mode.
    /// </summary>
    public async Task UpdateCasePriorityAsync(string fccmRequestId, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            UPDATE KDD_CASES kc
               SET kc.PRIORITY_CD = (
                    SELECT CASE WHEN a.RISK_CATEGORY_KEY = 1 THEN 'Low'
                                WHEN a.RISK_CATEGORY_KEY = 2 THEN 'Medium'
                                WHEN a.RISK_CATEGORY_KEY = 3 THEN 'High' END
                      FROM FCC_OB_RA a
                      INNER JOIN KDD_CASES b ON a.CASE_ID = b.CASE_INTRL_ID
                     WHERE a.REQUEST_ID = :p_req)
             WHERE kc.CASE_INTRL_ID = (SELECT CASE_ID FROM FCC_OB_RA WHERE REQUEST_ID = :p_req)";
        cmd.Parameters.Add(":p_req", OracleDbType.Varchar2).Value = fccmRequestId;
        await OpenAndPrepareAsync(con, ct);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        _log.LogDebug("KDD_CASES.PRIORITY_CD update: REQUEST_ID={RequestId} -> {Rows} row(s).", fccmRequestId, rows);
    }

    /// <summary>
    /// Legacy <c>caseInsertionController.UpdateRiskInRepo</c> first statement —
    /// syncs the KDD case-customer's name from the FCCM onboarding observation
    /// row (<c>fcc_ob_cust</c>). Mirrors (bound parameter):
    /// <code>
    /// UPDATE KDD_CASE_CUSTOMERS SET FULL_NM =
    ///   (SELECT FULL_NM FROM FCC_OB_CUST WHERE REQUEST_ID = :p_req)
    ///  WHERE CUST_INTRL_ID =
    ///   (SELECT EXISTING_CUST_INTRL_ID FROM FCC_OB_CUST WHERE REQUEST_ID = :p_req)
    /// </code>
    /// No-op in stub mode.
    /// </summary>
    public async Task SyncCaseCustomerNameAsync(string fccmRequestId, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            UPDATE KDD_CASE_CUSTOMERS
               SET FULL_NM = (SELECT FULL_NM FROM FCC_OB_CUST WHERE REQUEST_ID = :p_req)
             WHERE CUST_INTRL_ID = (SELECT EXISTING_CUST_INTRL_ID FROM FCC_OB_CUST WHERE REQUEST_ID = :p_req)";
        cmd.Parameters.Add(":p_req", OracleDbType.Varchar2).Value = fccmRequestId;
        await OpenAndPrepareAsync(con, ct);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        _log.LogDebug("KDD_CASE_CUSTOMERS.FULL_NM sync: REQUEST_ID={RequestId} -> {Rows} row(s).", fccmRequestId, rows);
    }

    /// <summary>
    /// Legacy <c>GetMultivalueMaxParam</c> Oracle half: given a set of candidate
    /// CODE_VALUEs (the TmX_Lookup hidden values collected from a KYC grid),
    /// returns the single CODE_VALUE with the highest SCORE in
    /// <c>FCC_OB_RISK_PRMS_CODE_SCRE_MAP</c>. Mirrors the legacy query
    /// <c>SELECT DISTINCT CODE_VALUE … WHERE SCORE = (SELECT MAX(SCORE) … WHERE
    /// CODE_VALUE IN (…)) AND CODE_VALUE IN (…)</c>, but with bound parameters
    /// instead of the legacy inline string concat. Returns null in stub mode,
    /// on empty input, or when no row matches.
    /// </summary>
    public async Task<string?> GetMaxScoreCodeValueAsync(
        IReadOnlyCollection<string> codeValues, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return null; }

        var values = codeValues
            .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "null", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        if (values.Count == 0) return null;

        // Build a parameterised IN list ( :v0, :v1, … ) reused in both clauses.
        var binds = values.Select((_, i) => $":v{i}").ToArray();
        var inList = string.Join(",", binds);

        // Best-effort: an Oracle outage or transient failure must NOT sink the whole
        // onboarding payload build. Legacy re-threw here; we log loudly and return null
        // so the affected risk field is simply omitted/left null and onboarding proceeds.
        try
        {
            await using var con = CreateConnection();
            await using var cmd = con.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText =
                $"SELECT DISTINCT CODE_VALUE FROM FCC_OB_RISK_PRMS_CODE_SCRE_MAP " +
                $"WHERE SCORE = (SELECT MAX(SCORE) FROM FCC_OB_RISK_PRMS_CODE_SCRE_MAP WHERE CODE_VALUE IN ({inList})) " +
                $"AND CODE_VALUE IN ({inList})";
            for (var i = 0; i < values.Count; i++)
                cmd.Parameters.Add(binds[i], OracleDbType.Varchar2).Value = values[i];

            await OpenAndPrepareAsync(con, ct);
            var result = await cmd.ExecuteScalarAsync(ct);
            var maxCode = result is null or DBNull ? null : result.ToString();
            _log.LogDebug("FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score: candidates=[{Codes}] -> {Chosen}",
                string.Join(",", values), maxCode ?? "(none)");
            return maxCode;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score lookup FAILED for candidates=[{Codes}]; " +
                "returning null so onboarding continues without this risk field.",
                string.Join(",", values));
            return null;
        }
    }

    // =====================================================================
    // Slice 9 — Pre-staging setup (TRUNCATE × 2 + sequence + RT_CASE_TRACKING)
    // =====================================================================

    /// <summary>
    /// <c>TRUNCATE TABLE RT_CASE_TRACKING</c>. Legacy clears this each
    /// submit; replicated for byte-for-byte parity. Note: TRUNCATE is
    /// destructive across all concurrent submits — the legacy assumes
    /// single-threaded submission, and we inherit that assumption.
    /// </summary>
    public async Task TruncateRtCaseTrackingAsync(CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE RT_CASE_TRACKING";
        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// <c>TRUNCATE TABLE EVENT_STAGING</c>. Same single-thread caveat
    /// as <see cref="TruncateRtCaseTrackingAsync"/>.
    /// </summary>
    public async Task TruncateEventStagingAsync(CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE EVENT_STAGING";
        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// <c>SELECT MESSAGE_ID_SEQ.nextval FROM DUAL</c>. Returns the next
    /// MessageId as a string (legacy reads it via <c>OracleDataReader</c>
    /// + <c>GetString</c> even though the column type is NUMBER — we
    /// match that contract).
    /// </summary>
    public async Task<string> GetNextMessageIdAsync(CancellationToken ct = default)
    {
        if (IsStub)
        {
            WarnOnceStub();
            return $"STUBSEQ-{Guid.NewGuid():N}";
        }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT MESSAGE_ID_SEQ.nextval FROM DUAL";
        await OpenAndPrepareAsync(con, ct);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString()
               ?? throw new InvalidOperationException("MESSAGE_ID_SEQ.nextval returned null.");
    }

    /// <summary>
    /// <c>INSERT INTO RT_CASE_TRACKING (MESSAGE_ID) VALUES (:p_msg)</c>.
    /// </summary>
    public async Task InsertRtCaseTrackingAsync(string messageId, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = "INSERT INTO RT_CASE_TRACKING (MESSAGE_ID) VALUES (:p_msg)";
        cmd.Parameters.Add(":p_msg", OracleDbType.Varchar2).Value = messageId;
        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =====================================================================
    // Slice 9 — Staging INSERTs (MANTAS schema)
    // =====================================================================

    /// <summary>
    /// Inserts one row into <c>MANTAS.BENEFICIARY_DETAIL_STAGING</c>.
    /// All columns parameterised; null fields stored as DBNull (the
    /// legacy schema allows nulls everywhere — see ModelContext.cs).
    /// </summary>
    public async Task InsertBeneficiaryDetailStagingAsync(BeneficiaryDetailStaging r, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            INSERT INTO MANTAS.BENEFICIARY_DETAIL_STAGING
                (MESSAGE_ID, PRODUCT, RECEIVERSBANK, INTERMEDBANK, BENEFICIARYCUSTNO,
                 BENIFICIARY, ADVISETHRUCUSTNO, ADVISETHRU, THIRDPARTYCUSTNO, THIRDPARTY,
                 ISSUINGBANK, BENEFICIARY, IMPORTERNAMEADDRESS, ADVISINGBKCUSTNO, ADVISINGBK,
                 BENEFICIARYID, BENEFICIARYCUST, BENEFCUST1, BENEFICIARYADDRESS,
                 SENDERCORR, RECEIVERSCORR, TRANSACTIONNUMBER)
            VALUES
                (:p_messageId, :p_product, :p_receiversbank, :p_intermedbank, :p_beneficiarycustno,
                 :p_benificiary, :p_advisethrucustno, :p_advisethru, :p_thirdpartycustno, :p_thirdparty,
                 :p_issuingbank, :p_beneficiary, :p_importernameaddress, :p_advisingbkcustno, :p_advisingbk,
                 :p_beneficiaryid, :p_beneficiarycust, :p_benefcust1, :p_beneficiaryaddress,
                 :p_sendercorr, :p_receiverscorr, :p_transactionnumber)";

        AddV(cmd, ":p_messageId",          r.MessageId);
        AddV(cmd, ":p_product",            r.Product);
        AddV(cmd, ":p_receiversbank",      r.Receiversbank);
        AddV(cmd, ":p_intermedbank",       r.Intermedbank);
        AddV(cmd, ":p_beneficiarycustno",  r.Beneficiarycustno);
        AddV(cmd, ":p_benificiary",        r.Benificiary);
        AddV(cmd, ":p_advisethrucustno",   r.Advisethrucustno);
        AddV(cmd, ":p_advisethru",         r.Advisethru);
        AddV(cmd, ":p_thirdpartycustno",   r.Thirdpartycustno);
        AddV(cmd, ":p_thirdparty",         r.Thirdparty);
        AddV(cmd, ":p_issuingbank",        r.Issuingbank);
        AddV(cmd, ":p_beneficiary",        r.Beneficiary);
        AddV(cmd, ":p_importernameaddress",r.Importernameaddress);
        AddV(cmd, ":p_advisingbkcustno",   r.Advisingbkcustno);
        AddV(cmd, ":p_advisingbk",         r.Advisingbk);
        AddV(cmd, ":p_beneficiaryid",      r.Beneficiaryid);
        AddV(cmd, ":p_beneficiarycust",    r.Beneficiarycust);
        AddV(cmd, ":p_benefcust1",         r.Benefcust1);
        AddV(cmd, ":p_beneficiaryaddress", r.Beneficiaryaddress);
        AddV(cmd, ":p_sendercorr",         r.Sendercorr);
        AddV(cmd, ":p_receiverscorr",      r.Receiverscorr);
        AddV(cmd, ":p_transactionnumber",  r.Transactionnumber);

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts one row into <c>MANTAS.CUST_MASTER_STAGING</c>.
    /// </summary>
    public async Task InsertCustMasterStagingAsync(CustMasterStaging r, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            INSERT INTO MANTAS.CUST_MASTER_STAGING
                (MESSAGE_ID, PRODUCT, ORDERINGCUST, PRESENTOR, APPLICANTCUSTNO,
                 APPLICANT, DRAWEECUSTNO, DRAWEE, CUSTOMERID, PRESENTORCUST,
                 ORDERINGBANK, CUSTOMER, DUMP_DT)
            VALUES
                (:p_messageId, :p_product, :p_orderingcust, :p_presentor, :p_applicantcustno,
                 :p_applicant, :p_draweecustno, :p_drawee, :p_customerid, :p_presentorcust,
                 :p_orderingbank, :p_customer, :p_dumpDt)";

        AddV(cmd, ":p_messageId",       r.MessageId);
        AddV(cmd, ":p_product",         r.Product);
        AddV(cmd, ":p_orderingcust",    r.Orderingcust);
        AddV(cmd, ":p_presentor",       r.Presentor);
        AddV(cmd, ":p_applicantcustno", r.Applicantcustno);
        AddV(cmd, ":p_applicant",       r.Applicant);
        AddV(cmd, ":p_draweecustno",    r.Draweecustno);
        AddV(cmd, ":p_drawee",          r.Drawee);
        AddV(cmd, ":p_customerid",      r.Customerid);
        AddV(cmd, ":p_presentorcust",   r.Presentorcust);
        AddV(cmd, ":p_orderingbank",    r.Orderingbank);
        AddV(cmd, ":p_customer",        r.Customer);
        cmd.Parameters.Add(":p_dumpDt", OracleDbType.Date).Value = r.DumpDt;

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts one row into <c>MANTAS.LC_TRN_DET_STAGING</c>. Slim
    /// trade-fields subset only — flag columns are intentionally NOT
    /// populated (Slice 8 catalogue model handles them via
    /// <see cref="InsertEventStagingAsync"/>).
    ///
    /// Legacy column naming preserved verbatim including the
    /// <c>PROUCT</c> typo on the product column.
    /// </summary>
    public async Task InsertLcTrnDetStagingAsync(LcTrnDetStaging r, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            INSERT INTO MANTAS.LC_TRN_DET_STAGING
                (MESSAGE_ID, PROUCT, FTID, DRID, WBID, MDID, LCID,
                 FTHSCODE, HSCODE, HSCODEOFGOODS, HSUNIT, HSQUANT, COMMODITY, QUANTITY, UNITCURRENCY,
                 LCAMOUNT, DEBITAMOUNT, CREDITAMNT, TOTEFORMAMT, DOCAMNT, EIFAMNT,
                 SELLUNITPRICE, FLRUNITPRICE, CAPUNITPRICE, VERSION,
                 COUNTRY, PORTOFLOADING, PORTOFDISCHARGE, SBPSHIPCITY, FINALDEST, FINALDESTINATION,
                 DESCGOODS, GOODSDESCRIPTION, SHIPDESPATCH, TRANSPORTATION, OTHERDESPATCHDET,
                 EXPIRYPLACE, ADDITIONALCOND, INSTRUCTIONS, LREMARKS)
            VALUES
                (:p_messageId, :p_prouct, :p_ftid, :p_drid, :p_wbid, :p_mdid, :p_lcid,
                 :p_fthscode, :p_hscode, :p_hscodeofgoods, :p_hsunit, :p_hsquant, :p_commodity, :p_quantity, :p_unitcurrency,
                 :p_lcamount, :p_debitamount, :p_creditamnt, :p_toteformamt, :p_docamnt, :p_eifamnt,
                 :p_sellunitprice, :p_flrunitprice, :p_capunitprice, :p_version,
                 :p_country, :p_portofloading, :p_portofdischarge, :p_sbpshipcity, :p_finaldest, :p_finaldestination,
                 :p_descgoods, :p_goodsdescription, :p_shipdespatch, :p_transportation, :p_otherdespatchdet,
                 :p_expiryplace, :p_additionalcond, :p_instructions, :p_lremarks)";

        AddV(cmd, ":p_messageId",        r.MessageId);
        AddV(cmd, ":p_prouct",           r.Prouct);                  // legacy typo
        AddV(cmd, ":p_ftid",             r.Ftid);
        AddV(cmd, ":p_drid",             r.Drid);
        AddV(cmd, ":p_wbid",             r.Wbid);
        AddV(cmd, ":p_mdid",             r.Mdid);
        AddV(cmd, ":p_lcid",             r.Lcid);
        AddV(cmd, ":p_fthscode",         r.Fthscode);
        AddV(cmd, ":p_hscode",           r.Hscode);
        AddV(cmd, ":p_hscodeofgoods",    r.Hscodeofgoods);
        AddV(cmd, ":p_hsunit",           r.Hsunit);
        AddD(cmd, ":p_hsquant",          r.Hsquant);
        AddV(cmd, ":p_commodity",        r.Commodity);
        AddD(cmd, ":p_quantity",         r.Quantity);
        AddV(cmd, ":p_unitcurrency",     r.Unitcurrency);
        AddD(cmd, ":p_lcamount",         r.Lcamount);
        AddD(cmd, ":p_debitamount",      r.Debitamount);
        AddD(cmd, ":p_creditamnt",       r.Creditamnt);
        AddD(cmd, ":p_toteformamt",      r.Toteformamt);
        AddD(cmd, ":p_docamnt",          r.Docamnt);
        AddD(cmd, ":p_eifamnt",          r.Eifamnt);
        AddD(cmd, ":p_sellunitprice",    r.Sellunitprice);
        AddD(cmd, ":p_flrunitprice",     r.Flrunitprice);
        AddD(cmd, ":p_capunitprice",     r.Capunitprice);
        AddD(cmd, ":p_version",          r.Version);
        AddV(cmd, ":p_country",          r.Country);
        AddV(cmd, ":p_portofloading",    r.Portofloading);
        AddV(cmd, ":p_portofdischarge",  r.Portofdischarge);
        AddV(cmd, ":p_sbpshipcity",      r.Sbpshipcity);
        AddV(cmd, ":p_finaldest",        r.Finaldest);
        AddV(cmd, ":p_finaldestination", r.Finaldestination);
        AddV(cmd, ":p_descgoods",        r.Descgoods);
        AddV(cmd, ":p_goodsdescription", r.Goodsdescription);
        AddV(cmd, ":p_shipdespatch",     r.Shipdespatch);
        AddV(cmd, ":p_transportation",   r.Transportation);
        AddV(cmd, ":p_otherdespatchdet", r.Otherdespatchdet);
        AddV(cmd, ":p_expiryplace",      r.Expiryplace);
        AddV(cmd, ":p_additionalcond",   r.Additionalcond);
        AddV(cmd, ":p_instructions",     r.Instructions);
        AddV(cmd, ":p_lremarks",         r.Lremarks);

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts one row into <c>MANTAS.EVENT_STAGING</c> (or
    /// schema-defaulted <c>EVENT_STAGING</c> if MANTAS is the login
    /// schema — legacy is inconsistent on this prefix; the SP reads
    /// whichever the connection's default schema points at). Called
    /// N+1 times per submit (1 customer event + N flag events).
    /// </summary>
    public async Task InsertEventStagingAsync(EventStaging r, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            INSERT INTO EVENT_STAGING
                (MSG_ID, INPUT_NAME, INPUT_MATCH, DESCRIPTION, TS_DATE, TITLE)
            VALUES
                (:p_msgId, :p_inputName, :p_inputMatch, :p_description, :p_tsDate, :p_title)";

        AddV(cmd, ":p_msgId",       r.MsgId);
        AddV(cmd, ":p_inputName",   r.InputName);
        AddV(cmd, ":p_inputMatch",  r.InputMatch);
        AddV(cmd, ":p_description", r.Description);
        cmd.Parameters.Add(":p_tsDate", OracleDbType.Date).Value =
            (object?)r.TsDate ?? DBNull.Value;
        AddV(cmd, ":p_title", r.Title);

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =====================================================================
    // Slice 9 — Stored procedure + post-SP enrichment
    // =====================================================================

    /// <summary>
    /// Calls the Oracle SP <c>PR_CREATE_EVENT_DEMO(MESSAGE_ID, PRODUCT,
    /// NUM, RESID OUT)</c>. The SP reads the staging tables filtered by
    /// MessageId and builds the FCCM case + observations. Returns the
    /// new case id via the <c>RESID</c> output parameter.
    /// </summary>
    public async Task<string?> ExecuteCreateEventDemoProcAsync(
        string messageId, string product, CancellationToken ct = default)
    {
        if (IsStub)
        {
            WarnOnceStub();
            var stubCaseId = $"STUB-{messageId}";
            _log.LogInformation(
                "ExecuteCreateEventDemoProcAsync (STUB) — MessageId={MsgId} Product={Product} → synthetic CaseId={CaseId}",
                messageId, product, stubCaseId);
            return stubCaseId;
        }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName  = true;
        cmd.CommandText = "PR_CREATE_EVENT_DEMO";
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add("MESSAGE_ID", OracleDbType.Varchar2).Value = messageId;
        cmd.Parameters.Add("PRODUCT",    OracleDbType.Varchar2).Value = product;
        cmd.Parameters.Add("NUM",        OracleDbType.Int32).Value    = 1;
        var resid = cmd.Parameters.Add("RESID", OracleDbType.Varchar2, 50);
        resid.Direction = ParameterDirection.Output;

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);

        return resid.Value?.ToString();
    }

    /// <summary>
    /// Post-SP enrichment — attach the customer to the case. Hardcoded
    /// constants <c>JRSDCN_CD='PAK'</c>, <c>CUST_TYPE_CD='ORG'</c>,
    /// <c>BUS_DMN_LIST_TX='abc'</c> preserved from legacy.
    /// </summary>
    public async Task InsertKddCaseCustomersAsync(
        string customerName, string clientReferenceId, int transactionId, string caseId,
        CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            INSERT INTO KDD_CASE_CUSTOMERS
                (FULL_NM, CUST_INTRL_ID, JRSDCN_CD, CASE_CUST_SEQ_ID, CASE_INTRL_ID, CUST_TYPE_CD, BUS_DMN_LIST_TX)
            VALUES
                (:p_fullNm, :p_clientRef, 'PAK', :p_txnId, :p_caseId, 'ORG', 'abc')";

        AddV(cmd, ":p_fullNm",    customerName);
        AddV(cmd, ":p_clientRef", clientReferenceId);
        cmd.Parameters.Add(":p_txnId",  OracleDbType.Int32).Value    = transactionId;
        AddV(cmd, ":p_caseId",    caseId);

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Post-SP enrichment — set the case title to the transaction
    /// number.
    /// </summary>
    public async Task UpdateKddCasesTitleAsync(
        string caseId, string transactionNumber, CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText =
            "UPDATE KDD_CASES SET CASE_TITL_NM = :p_title WHERE CASE_INTRL_ID = :p_caseId";
        AddV(cmd, ":p_title",  transactionNumber);
        AddV(cmd, ":p_caseId", caseId);

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Correlation row — links the FCCM case back to the originating
    /// transaction. Legacy used inline string concat (SQL injection
    /// risk); we use bound parameters.
    /// </summary>
    public async Task InsertTransFromRepoAsync(
        string?  customerName,
        string?  clientReferenceNumber,
        int      transactionId,
        string?  transactionNumber,
        CancellationToken ct = default)
    {
        if (IsStub) { WarnOnceStub(); return; }

        await using var con = CreateConnection();
        await using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"
            INSERT INTO TMX_TRANS_FROM_REPO
                (CUSTOMERNAME, CLIENTREFNUM, TRANSACTION_ID, TRANSACTIONO)
            VALUES
                (:p_customer, :p_clientRef, :p_txnId, :p_txnNum)";

        AddV(cmd, ":p_customer",  customerName);
        AddV(cmd, ":p_clientRef", clientReferenceNumber);
        cmd.Parameters.Add(":p_txnId", OracleDbType.Int32).Value = transactionId;
        AddV(cmd, ":p_txnNum",    transactionNumber);

        await OpenAndPrepareAsync(con, ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>Add a nullable-string varchar parameter (DBNull on null/empty).</summary>
    private static void AddV(OracleCommand cmd, string name, string? value)
    {
        cmd.Parameters.Add(name, OracleDbType.Varchar2).Value =
            string.IsNullOrEmpty(value) ? DBNull.Value : (object)value;
    }

    /// <summary>Add a nullable-decimal parameter (DBNull on null).</summary>
    private static void AddD(OracleCommand cmd, string name, decimal? value)
    {
        cmd.Parameters.Add(name, OracleDbType.Decimal).Value =
            value.HasValue ? (object)value.Value : DBNull.Value;
    }

    private void WarnOnceStub()
    {
        if (_warnedAboutStub) return;
        _log.LogWarning(
            "FccmOracleReader is in STUB mode — Integration:FccmOracleConnection is unset. " +
            "All FCCM Oracle calls (sequence read, staging INSERTs, SP, KDD updates, correlation insert) " +
            "no-op and return synthetic values. Set the connection string in appsettings to wire real Oracle.");
        _warnedAboutStub = true;
    }
}
