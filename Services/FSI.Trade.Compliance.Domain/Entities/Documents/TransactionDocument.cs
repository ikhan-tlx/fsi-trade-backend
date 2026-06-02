namespace FSI.Trade.Compliance.Domain.Entities.Documents;

/// <summary>
/// Slice 10 §7.4 — bridge between <c>TmX_Transaction</c> and
/// <c>TmX_Document</c>. Many-to-many: the same document can be referenced
/// by multiple transactions (rare, but legitimate — e.g. corporate
/// pre-approval letter applied to multiple LCs); a transaction can have
/// many documents.
///
/// Why a bridge instead of a direct FK on Document
/// -----------------------------------------------
/// Keeps <c>TmX_Document</c> polymorphic. Documents already live behind
/// <c>TmX_Transaction_Flag.Evidence_Document_ID</c> for flag evidence; a
/// bare <c>Transaction_ID</c> column on Document would couple the
/// attachment store to one consumer module. Future consumers (customer
/// docs, report exports, audit attachments) get their own linkage table.
///
/// Schema source: <c>database/migrations/2026_05_027_CreateTransactionDocumentBridge.sql</c>.
/// </summary>
public class TransactionDocument
{
    public int       TransactionDocumentId  { get; set; }
    public int       TransactionId          { get; set; }
    public int       DocumentId             { get; set; }

    public int       TenantId               { get; set; } = 1;
    public bool      ActiveFlag             { get; set; } = true;

    public string    CreatedBy              { get; set; } = "";
    public DateTime  CreatedDate            { get; set; }
    public string?   LastUpdatedBy          { get; set; }
    public DateTime? LastUpdatedDate        { get; set; }
}
