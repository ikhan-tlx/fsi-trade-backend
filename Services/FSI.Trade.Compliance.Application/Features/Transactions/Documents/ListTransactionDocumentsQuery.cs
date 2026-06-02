using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Documents;

/// <summary>
/// Slice 10 §7.4 — lists documents directly attached to a transaction
/// (TmX_Document rows with Transaction_ID matching). Flag-evidence
/// documents are NOT included — the Flags panel already surfaces those
/// via the per-flag <c>evidenceDocumentId</c>.
/// </summary>
public record ListTransactionDocumentsQuery(int TransactionId) : IRequest<IReadOnlyList<TransactionDocumentDto>>;

public class TransactionDocumentDto
{
    public int      documentId        { get; set; }
    public string   originalFileName  { get; set; } = "";
    public string?  mimeType          { get; set; }
    public long?    fileSizeBytes     { get; set; }
    public string   uploadedBy        { get; set; } = "";
    public string?  uploadedByName    { get; set; }
    public DateTime uploadedDate      { get; set; }

    /// <summary>Stable download URL — points to the generic document endpoint.</summary>
    public string   downloadUrl       { get; set; } = "";
}
