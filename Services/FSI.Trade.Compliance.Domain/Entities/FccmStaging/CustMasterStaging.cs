namespace FSI.Trade.Compliance.Domain.Entities.FccmStaging;

/// <summary>
/// Plain POCO for the legacy <c>CUST_MASTER_STAGING</c> table on the
/// FCCM Oracle DB (schema <c>MANTAS</c>). Per-transaction customer
/// snapshot used by the Oracle SP to populate the case's customer entity.
///
/// NOT EF-mapped — see <c>EventStaging.cs</c> header for the rationale.
/// </summary>
public class CustMasterStaging
{
    public string?   MessageId       { get; set; }
    public string?   Product         { get; set; }
    public string?   Orderingcust    { get; set; }
    public string?   Presentor       { get; set; }
    public string?   Applicantcustno { get; set; }
    public string?   Applicant       { get; set; }
    public string?   Draweecustno    { get; set; }
    public string?   Drawee          { get; set; }
    public string?   Customerid      { get; set; }
    public string?   Presentorcust   { get; set; }
    public string?   Orderingbank    { get; set; }
    public string?   Customer        { get; set; }
    public DateTime  DumpDt          { get; set; }
}
