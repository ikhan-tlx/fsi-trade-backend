namespace FSI.Trade.Compliance.Domain.Entities.Transaction;

/// <summary>
/// One-to-many under <see cref="Transaction"/>. Same UDF-JSON shape — used
/// for the "Stakeholders" repeating section in the dynamic form.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 8214.
/// </summary>
public class TransactionStakeholder
{
    public int       TransactionStakeholderId { get; set; }
    public int       TransactionId            { get; set; }
    public int       TenantId                 { get; set; }
    public string?   UdfData                  { get; set; }

    public string    CreatedBy                { get; set; } = "";
    public DateTime  CreatedDate              { get; set; }
    public string?   LastUpdatedBy            { get; set; }
    public DateTime? LastUpdatedDate          { get; set; }
}
