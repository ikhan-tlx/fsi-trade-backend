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

    /// <summary>BRAINS HTTP screening service base URL (legacy: <c>BRAINSKYCUrl</c>).</summary>
    public string BrainsKycBaseUrl       { get; set; } = "";

    /// <summary>Customer master HTTP service base URL (legacy: gateway <c>DLP/GetCustomerId</c>).</summary>
    public string CustomerMasterBaseUrl  { get; set; } = "";

    /// <summary>FCCM HTTP onboarding endpoint for case submission (legacy: <c>KYCOnboardingURL</c>).</summary>
    public string FccmOnboardingUrl      { get; set; } = "";

    /// <summary>
    /// FCCM Oracle DB connection string for poller reads from <c>FCC_OB_RA</c>.
    /// Empty string = poller skips Oracle reads (stub mode), useful in dev.
    /// </summary>
    public string FccmOracleConnection   { get; set; } = "";

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
}
