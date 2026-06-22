namespace FSI.Trade.Compliance.Application.Common.Options;

public enum BrainsMode
{
    /// <summary>Call the real BRAINS QueryKycInfoServlet over HTTP.</summary>
    Live,
    /// <summary>Return a canned sample response (UI sanity while localhost BRAINS is down).</summary>
    Sandbox
}

/// <summary>
/// BRAINS↔TBML real-time KYC query interface (v2.0 —
/// <c>QueryKycInfoServlet</c>). Bound from <c>Integration:Brains</c>.
///
/// Request (GET query-string). Matching the production legacy
/// (INT IcbcService.GetKYC, verified from its int.log): CUSTNO is the customer
/// code passed per call; ZONENO / ADJUST_TYPE are configurable fixed defaults
/// (6001 / 1); APPLY_NO and TASK_NO are GENERATED per request (FileTime); and
/// REG_TYPE / REG_NO are sent EMPTY — production keys the lookup on CUSTNO
/// alone and never populated them.
/// </summary>
public class BrainsOptions
{
    public const string SectionName = "Integration:Brains";

    /// <summary>Live = real HTTP; Sandbox = canned response (prod start-up is blocked when Sandbox).</summary>
    public BrainsMode Mode { get; set; } = BrainsMode.Live;

    /// <summary>QueryKycInfoServlet URL, e.g. http://localhost:8080/icbc/brainsEU/servlet/QueryKycInfoServlet</summary>
    public string BaseUrl { get; set; } = "";

    // ---- Mandatory request fields (configurable defaults) ----

    /// <summary>ZONENO — mandatory, fixed value 6001 per the interface doc.</summary>
    public string ZoneNo     { get; set; } = "6001";

    /// <summary>ADJUST_TYPE — 0 = new customer, 1 = existing customer. Trade flow = existing.</summary>
    public string AdjustType { get; set; } = "1";

    // REG_TYPE / REG_NO are intentionally NOT configurable: the production
    // legacy (INT IcbcService.GetKYC) sends them EMPTY and keys the BRAINS
    // lookup on CUSTNO alone. TASK_NO and APPLY_NO are generated per request
    // (see BrainsKycScreeningService.BuildUrl).

    // ---- Sandbox knobs ----

    /// <summary>Optional path to a JSON file holding a canned BRAINS response. Relative to ContentRoot.</summary>
    public string? SandboxResponseFile { get; set; }

    /// <summary>
    /// Optional — force the sandbox STATUS (0=PEP / 1=High / 2=Medium / 3=Low),
    /// or "fail" to simulate TRANSOK=1. When blank, the sandbox derives a
    /// deterministic band from the customer code so QA can exercise all bands.
    /// </summary>
    public string? SandboxForceStatus  { get; set; }
}
