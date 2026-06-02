namespace FSI.Trade.Compliance.Domain.Entities.FccmStaging;

/// <summary>
/// Plain POCO for the legacy <c>BENEFICIARY_DETAIL_STAGING</c> table
/// on the FCCM Oracle DB (schema <c>MANTAS</c>). Per-transaction
/// counterparty / bank snapshot used by the Oracle SP to populate
/// case-side beneficiary observations.
///
/// All fields are strings — the legacy schema doesn't enforce types
/// because FCCM-side ETL handles parsing.
///
/// NOT EF-mapped — see <c>EventStaging.cs</c> header for the rationale.
/// </summary>
public class BeneficiaryDetailStaging
{
    public string?   MessageId           { get; set; }
    public string?   Product             { get; set; }
    public string?   Receiversbank       { get; set; }
    public string?   Intermedbank        { get; set; }
    public string?   Beneficiarycustno   { get; set; }
    public string?   Benificiary         { get; set; }
    public string?   Advisethrucustno    { get; set; }
    public string?   Advisethru          { get; set; }
    public string?   Thirdpartycustno    { get; set; }
    public string?   Thirdparty          { get; set; }
    public string?   Issuingbank         { get; set; }
    public string?   Beneficiary         { get; set; }
    public string?   Importernameaddress { get; set; }
    public string?   Advisingbkcustno    { get; set; }
    public string?   Advisingbk          { get; set; }
    public string?   Beneficiaryid       { get; set; }
    public string?   Beneficiarycust     { get; set; }
    public string?   Benefcust1          { get; set; }
    public string?   Beneficiaryaddress  { get; set; }
    public string?   Sendercorr          { get; set; }
    public string?   Receiverscorr       { get; set; }
    public string?   Transactionnumber   { get; set; }
}
