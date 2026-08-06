namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Third-party real-time KYC query interface (v2.0 <c>QueryKycInfoServlet</c>),
/// bound from <c>Integration:Brains</c>. The app is ALWAYS live (there is no
/// in-process sandbox mode any more — screening is turned on/off via
/// Configuration Management's <c>Onboarding.RequireExternalVerification</c>, and
/// non-production environments point <see cref="BaseUrl"/> at the standalone
/// BRAINS-compatible provider in <c>kyc-mock-provider/</c>).
///
/// Request (GET query-string), matching the production legacy (INT
/// IcbcService.GetKYC): CUSTNO is the customer code passed per call; ZONENO /
/// ADJUST_TYPE are configurable fixed defaults (6001 / 1); APPLY_NO and TASK_NO
/// are GENERATED per request (FileTime); REG_TYPE / REG_NO are sent EMPTY —
/// the provider keys the lookup on CUSTNO alone.
/// </summary>
public class KycScreeningOptions
{
    public const string SectionName = "Integration:Brains";

    /// <summary>QueryKycInfoServlet URL (the live provider, or the local test provider).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>ZONENO — mandatory, fixed value 6001 per the interface doc.</summary>
    public string ZoneNo     { get; set; } = "6001";

    /// <summary>ADJUST_TYPE — 0 = new customer, 1 = existing customer. Trade flow = existing.</summary>
    public string AdjustType { get; set; } = "1";

    // REG_TYPE / REG_NO are intentionally NOT configurable: the production legacy
    // sends them EMPTY and keys the lookup on CUSTNO alone. TASK_NO / APPLY_NO are
    // generated per request (see KycScreeningService.BuildUrl).
}
