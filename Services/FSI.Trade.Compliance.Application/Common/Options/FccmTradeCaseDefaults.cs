namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Environment/tenant-configurable constants for the trade FCCM case + event
/// build. Each value maps to an IN parameter of the parameterised Oracle proc
/// <c>PR_CREATE_EVENT_DEMO_CFG</c> and replaces a value that used to be hardcoded
/// inside <c>PR_CREATE_EVENT_DEMO</c>. Defaults below reproduce the original
/// hardcoded behaviour, so an environment that doesn't override anything behaves
/// exactly as before. Bound from <c>Integration:TradeCaseDefaults</c>.
/// </summary>
public class FccmTradeCaseDefaults
{
    /// <summary>Jurisdiction code — proc <c>JRSDCN_CD</c> / event <c>V_JURISDICTION_CD</c>. Legacy: <c>PAK</c>.</summary>
    public string Jurisdiction     { get; set; } = "PAK";

    /// <summary>Owning organisation — KDD_CASES <c>OWNER_ORG</c>. Legacy: <c>ICBC</c>.</summary>
    public string OwnerOrg         { get; set; } = "ICBC";

    /// <summary>Case owner / assignee user id — KDD_CASES <c>OWNER_ID</c>/<c>ORIG_OWNER_ID</c>/<c>ASSIGNED_*_ID</c>. Legacy: <c>10004</c>.</summary>
    public int    OwnerId          { get; set; } = 10004;

    /// <summary>Business domain — event <c>V_BUSINESS_DOMAIN_CD</c>. Legacy: <c>GEN</c>.</summary>
    public string BusinessDomain   { get; set; } = "GEN";

    /// <summary>Event type — FCC_EVENTS <c>V_EVENT_TYPE</c>. Legacy: <c>TBAML</c>.</summary>
    public string EventType        { get; set; } = "TBAML";

    /// <summary>Scenario class — FCC_EVENTS <c>V_SCENARIO_CLASS</c>. Legacy: <c>TBML</c>.</summary>
    public string ScenarioClass    { get; set; } = "TBML";

    /// <summary>Case type / subtype — KDD_CASES <c>CASE_TYPE_CD</c>/<c>CASE_TYPE_SUBTYPE_CD</c>. Legacy: <c>TBML_SURV</c>.</summary>
    public string CaseType         { get; set; } = "TBML_SURV";

    /// <summary>Data origin / source system / processing batch marker — many FCC/KDD columns. Legacy: <c>DLY</c>.</summary>
    public string DataOrigin       { get; set; } = "DLY";

    /// <summary>Event status code — FCC_EVENTS <c>V_STATUS_CD</c>. Legacy: <c>CLS</c>.</summary>
    public string EventStatus      { get; set; } = "CLS";

    /// <summary>Event priority — FCC_EVENTS <c>V_EVENT_PRIORITY</c>. Legacy: <c>HIGH</c>.</summary>
    public string EventPriority    { get; set; } = "HIGH";

    /// <summary>Case priority — KDD_CASES <c>PRIORITY_CD</c>. Legacy: <c>High</c>.</summary>
    public string CasePriority     { get; set; } = "High";

    /// <summary>Case source code — KDD_CASES <c>CASE_SRC_CD</c>. Legacy: <c>CR</c>.</summary>
    public string CaseSource       { get; set; } = "CR";

    /// <summary>Initial case status — KDD_CASES <c>STATUS_CD</c>. Legacy: <c>NW</c>.</summary>
    public string CaseStatus       { get; set; } = "NW";

    /// <summary>Contract event type code — KDD_CASE_TRD_FIN_CNTRCT_EVT <c>CNTRCT_EVENT_TYPE_CD</c>. Legacy: <c>ISSU</c>.</summary>
    public string ContractEventType { get; set; } = "ISSU";

    /// <summary>Case-customer type code — KDD_CASE_CUSTOMERS <c>CUST_TYPE_CD</c> (app-side enrichment insert). Legacy: <c>ORG</c>.</summary>
    public string CustomerType     { get; set; } = "ORG";

    /// <summary>Case-customer business-domain list — KDD_CASE_CUSTOMERS <c>BUS_DMN_LIST_TX</c> (app-side enrichment insert). Legacy: <c>abc</c>.</summary>
    public string CustomerBusinessDomainList { get; set; } = "abc";
}
