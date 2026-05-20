namespace FSI.Trade.Compliance.Domain.Entities.Transaction;

/// <summary>
/// One-to-many under <see cref="Transaction"/>. Same UDF-JSON shape as
/// <see cref="TransactionDetail"/> — used by the FE's "Beneficiary" repeating
/// section in the dynamic form.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 2656.
/// </summary>
public class BeneficiaryDetail
{
    public int       BeneficiaryDetailId   { get; set; }
    public int       TransactionId         { get; set; }
    public int       TenantId              { get; set; }
    public string?   UdfData               { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}
