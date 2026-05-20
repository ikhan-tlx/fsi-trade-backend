namespace FSI.Trade.Compliance.Domain.Entities.Customer;

/// <summary>
/// One-to-many under <see cref="CustomerMaster"/>. Banking accounts and
/// channel preferences (cheque book / internet banking limits / card #).
/// The dynamic-form renderer surfaces a subset of these as fields, the
/// rest are stored as <c>UDF_Data</c> JSON on the same row.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 5472.
/// </summary>
public class CustomerBankingDetail
{
    public int       CustomerBankingDetailId         { get; set; }
    public int?      CustomerMasterId                { get; set; }
    public int       TenantId                        { get; set; }
    public bool      ActiveFlag                      { get; set; }
    public DateTime  EffectiveStartDate              { get; set; }
    public DateTime  EffectiveEndDate                { get; set; }

    public string?   BankAccountNumber               { get; set; }
    public string?   BankCardNumber                  { get; set; }
    public string?   CardMemberName                  { get; set; }
    public string?   BranchCode                      { get; set; }
    public string?   ChequeBookNumber                { get; set; }
    public int?      AddressTypeLkpId                { get; set; }
    public int?      InternetBanking                 { get; set; }
    public decimal?  InternetBankingTransactionAmount{ get; set; }
    public decimal?  InternetAtmTransactionAmount    { get; set; }
    public string?   MailingCommunication            { get; set; }

    public string?   UdfData                         { get; set; }

    public string    CreatedBy                       { get; set; } = "";
    public DateTime  CreatedDate                     { get; set; }
    public string?   LastUpdatedBy                   { get; set; }
    public DateTime? LastUpdatedDate                 { get; set; }
}
