namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Slice 4 — upstream URLs and tunables for the BRAINS / Customer / FCCM
/// adapters. Bound from the <c>Integration</c> section in appsettings.json.
/// All URLs default to placeholders; production deployments override via
/// configuration / secrets store.
/// </summary>
public class IntegrationOptions
{
    public const string SectionName = "Integration";

    // BrainsKycBaseUrl removed 2026-06 — BRAINS now uses the v2.0 servlet
    // configured under Integration:Brains (BrainsOptions.BaseUrl).

    /// <summary>Customer master HTTP service base URL (legacy: gateway <c>DLP/GetCustomerId</c>).</summary>
    public string CustomerMasterBaseUrl  { get; set; } = "";

    /// <summary>FCCM HTTP onboarding endpoint for case submission (legacy: <c>KYCOnboardingURL</c>).</summary>
    public string FccmOnboardingUrl      { get; set; } = "";

    /// <summary>
    /// HTTP Basic-auth username for the FCCM onboarding endpoint (legacy:
    /// <c>KYCUsername</c>). The legacy controller built
    /// <c>Authorization: Basic base64(user:pass)</c> from these. Empty =
    /// no auth header sent.
    /// </summary>
    public string FccmOnboardingUsername { get; set; } = "";

    /// <summary>HTTP Basic-auth password for the FCCM onboarding endpoint (legacy: <c>KYCPassword</c>).</summary>
    public string FccmOnboardingPassword { get; set; } = "";

    /// <summary>
    /// Environment-configurable onboarding settings (auth + payload defaults +
    /// field-inclusion toggles). Bound from <c>Integration:Onboarding</c>.
    /// Prefer these over the deprecated top-level
    /// <see cref="FccmOnboardingUsername"/> / <see cref="FccmOnboardingPassword"/>
    /// (kept only as a fallback). This block makes an FCCM environment switch a
    /// config change rather than a code edit — see
    /// <see cref="FccmOnboardingOptions"/>.
    /// </summary>
    public FccmOnboardingOptions Onboarding { get; set; } = new();

    /// <summary>
    /// FCCM Oracle DB connection string — used for EVERYTHING FCCM:
    /// case-id poll reads (<c>FCC_OB_RA</c>), staging-table INSERTs
    /// (<c>MANTAS.*_STAGING</c>), the SP call
    /// (<c>PR_CREATE_EVENT_DEMO</c>), and post-SP enrichment
    /// (<c>KDD_CASE_CUSTOMERS</c> / <c>KDD_CASES</c>). One connection
    /// string, one Oracle DB. Empty = all FCCM operations log a STUB
    /// warning and return synthetic values (dev mode).
    ///
    /// Slice 9 rework: previously had a sibling
    /// <c>FccmStagingConnection</c> for a non-existent SQL Server
    /// staging DB. Confirmed all staging lives on the FCCM Oracle —
    /// see docs/LEGACY_FCCM_TRADE_FLOW.md §0.
    /// </summary>
    public string FccmOracleConnection   { get; set; } = "";

    /// <summary>
    /// Oracle schema the FCCM objects live in. The reader references most
    /// objects UNQUALIFIED (RT_CASE_TRACKING, EVENT_STAGING, MESSAGE_ID_SEQ,
    /// FCC_OB_RA, PR_CREATE_EVENT_DEMO, KDD_CASES, KDD_CASE_CUSTOMERS,
    /// TMX_TRANS_FROM_REPO) plus a few <c>MANTAS.</c>-qualified staging tables.
    /// On connection open the reader runs <c>ALTER SESSION SET CURRENT_SCHEMA</c>
    /// to this value so the unqualified names resolve correctly even when the
    /// login user isn't the schema owner (legacy logged in AS <c>mantas</c>;
    /// we can log in as e.g. <c>ofsaafccm</c> and still target MANTAS).
    /// Default "MANTAS". Empty = use the login user's default schema.
    /// </summary>
    public string FccmOracleSchema       { get; set; } = "MANTAS";

    /// <summary>
    /// HMAC shared secret used to validate inbound FCCM callbacks. Required —
    /// the callback endpoint is anonymous (FCCM has no JWT) so we authenticate
    /// via signed payload header. Treat as a secret; rotate on incident.
    /// </summary>
    public string FccmCallbackSharedSecret { get; set; } = "";

    /// <summary>
    /// How often the FccmCaseIdPoller wakes up. Default 5 seconds — fast
    /// enough to feel "near real-time" to the FE, slow enough to not stress
    /// the upstream Oracle.
    /// </summary>
    public TimeSpan PollingInterval      { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait for a case ID before flipping the request to
    /// <c>Timeout</c>. Default 5 minutes.
    /// </summary>
    public TimeSpan CaseIdTimeout        { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>HTTP timeout per outbound call (BRAINS, Customer master, FCCM submit). Default 30s.</summary>
    public TimeSpan HttpTimeout          { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Slice 14 — maps FCCM action codes to <c>APPLICATION_STATUS</c>
    /// lookup names that the callback handler resolves and writes to
    /// <c>TmX_Transaction.Transaction_Status_Lkp</c>. The legacy
    /// <c>updateTransactionStatusAPIController</c> hardcoded the mapping
    /// (30004→3057 Approved, 30003→3058 Rejected, 983/984 same); this
    /// makes the mapping a config switch so action codes can be added /
    /// re-pointed without a redeploy.
    ///
    /// <para>
    /// Values are <c>Visible_Value</c> strings on <c>TmX_Lookup</c> where
    /// <c>Lookup_Type = 'APPLICATION_STATUS'</c>. The handler does a
    /// cross-tenant cascade lookup at runtime (caller's tenant first,
    /// fall back to Tenant 1), so tenant-specific Lookup_IDs are picked
    /// up automatically — no hardcoded 3057 / 3058 anywhere.
    /// </para>
    ///
    /// <para>
    /// Defaults below mirror the legacy contract. Override in
    /// appsettings.json under <c>Integration:FccmActionCodeStatusMap</c>
    /// if FCCM changes its code set.
    /// </para>
    /// </summary>
    public Dictionary<string, string> FccmActionCodeStatusMap { get; set; } = new()
    {
        { "30004", "APPROVED" },
        { "30003", "REJECTED" },
        { "983",   "APPROVED" },
        { "984",   "REJECTED" }
    };
}
