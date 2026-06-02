namespace FSI.Trade.Compliance.Domain.Entities.FccmStaging;

/// <summary>
/// Plain POCO for the legacy <c>CUST_BANKING_STAGING</c> table on the
/// FCCM Oracle DB (schema <c>MANTAS</c>). Per-transaction banking-
/// account snapshot used by the Oracle SP for case-side banking
/// observations. Slice 9 added this entity after the original Phase 2
/// pass missed it — the legacy <c>ModelContext.cs</c> declares it
/// alongside the other four staging entities.
///
/// Today's <c>FccmTradeCaseService</c> doesn't populate this table
/// (no banking data in the new transaction's domain — it lives on
/// <c>TmX_Customer_Banking_Details</c> but isn't currently surfaced
/// per-transaction). Field set captured here so a future slice can
/// wire the population without re-modeling the schema.
///
/// NOT EF-mapped — see <c>EventStaging.cs</c> header for the rationale.
/// </summary>
public class CustBankingStaging
{
    public string?   MessageId        { get; set; }
    public string?   Product          { get; set; }
    public string?   AvailableWith    { get; set; }
    public string?   BeneficiaryBank  { get; set; }
    public string?   IntermedBank     { get; set; }
    public string?   ReceiversBank    { get; set; }
    public string?   IssuingBank      { get; set; }
    public string?   AdvisingBank     { get; set; }
    public string?   SenderCorr       { get; set; }
    public string?   ReceiversCorr    { get; set; }
    public string?   AccountWith      { get; set; }
    public string?   OrderingBank     { get; set; }
}
