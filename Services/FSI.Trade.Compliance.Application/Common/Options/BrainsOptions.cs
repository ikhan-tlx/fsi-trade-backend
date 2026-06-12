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
/// Request (GET query-string) — the BE sends ONLY the mandatory fields:
///   TASK_NO, ZONENO, ADJUST_TYPE, CUSTNO, REG_TYPE, REG_NO.
/// Of these, ZONENO / ADJUST_TYPE / REG_TYPE / REG_NO are configurable
/// defaults (per FSI direction, 2026-06); CUSTNO is the customer code passed
/// per call, and TASK_NO defaults to the customer code when not configured.
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

    /// <summary>REG_TYPE — identifier type (char 3). Configurable default.</summary>
    public string RegType    { get; set; } = "";

    /// <summary>REG_NO — identifier number. Configurable default.</summary>
    public string RegNo      { get; set; } = "";

    /// <summary>TASK_NO — the task id sent by TBML. When blank, the customer code is used.</summary>
    public string? TaskNo    { get; set; }

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
