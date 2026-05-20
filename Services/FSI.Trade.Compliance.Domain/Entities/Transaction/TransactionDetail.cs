namespace FSI.Trade.Compliance.Domain.Entities.Transaction;

/// <summary>
/// One-to-one with <see cref="Transaction"/>. Holds the JSON-shaped UDF
/// payload the dynamic-form renderer reads/writes. Stored as
/// <c>nvarchar(max)</c> in the DB; we parse it server-side on read so the
/// FE gets a structured dictionary.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 2636.
/// </summary>
public class TransactionDetail
{
    public int       TransactionDetailId   { get; set; }
    public int       TransactionId         { get; set; }
    public int       TenantId              { get; set; }
    public string?   UdfData               { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}
