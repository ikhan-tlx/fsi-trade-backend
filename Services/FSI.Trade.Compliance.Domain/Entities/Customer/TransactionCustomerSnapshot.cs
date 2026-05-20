namespace FSI.Trade.Compliance.Domain.Entities.Customer;

/// <summary>
/// Per-transaction customer snapshot. Despite the underlying table being
/// named <c>TmX_Customer_Master</c> in the DB, this is NOT a "customer
/// master of record" — it's a snapshot of customer attributes captured on
/// each transaction. The real customer master lives upstream in
/// <b>FCCM</b> (Oracle) and <b>BRAINS</b>, accessed via the Slice 4 adapters.
/// One row per <c>Transaction_Id</c>; the same logical customer
/// (<c>Customer_Code</c>) can appear many times in this table — once per
/// transaction created against them.
///
/// Renamed from <c>CustomerMaster</c> in Slice 6 Step 3 for naming accuracy.
/// The DB table name stays as <c>TmX_Customer_Master</c> — only the C#
/// entity / EF config / DbSet are renamed.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 1149.
/// </summary>
public class TransactionCustomerSnapshot
{
    public int       CustomerMasterId           { get; set; }   // primary key column kept as Customer_Master_Id in DB
    public int       TenantId                   { get; set; }
    public int?      TransactionId              { get; set; }

    public string?   CustomerCode               { get; set; }
    public string?   CustomerTitle              { get; set; }
    public string?   CustomerName               { get; set; }
    public int?      CustomerTypeLkp            { get; set; }
    public int?      CustomerClassificationLkp  { get; set; }
    public int?      CustomerStatusLkp          { get; set; }
    public int?      NationalIdTypeLkp          { get; set; }
    public string?   NationalIdentifierValue    { get; set; }
    public int?      CustomerSegmentLkp         { get; set; }
    public int?      CustomerSubSegmentLkp      { get; set; }
    public int?      EntityTypeLkp              { get; set; }
    public int?      FatcaClassLkp              { get; set; }
    public int?      RelationshipCodeLkp        { get; set; }
    public int?      LocationId                 { get; set; }

    public string?   UdfData                    { get; set; }

    public string    CreatedBy                  { get; set; } = "";
    public DateTime  CreatedDate                { get; set; }
    public string?   LastUpdatedBy              { get; set; }
    public DateTime? LastUpdatedDate            { get; set; }
}
