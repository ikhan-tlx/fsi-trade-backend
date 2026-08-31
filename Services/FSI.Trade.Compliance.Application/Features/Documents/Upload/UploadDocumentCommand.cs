using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Documents.Upload;

/// <summary>
/// Persists a file via <c>IDocumentStorage</c> and inserts the
/// matching <c>TmX_Document</c> row. The controller reads the
/// <c>IFormFile</c> and hands off the raw stream so the
/// MediatR handler stays HTTP-shape-agnostic.
///
/// Maps to <c>POST /api/v1/Document</c> (multipart/form-data).
/// </summary>
public class UploadDocumentCommand : IRequest<UploadDocumentResult>
{
    public Stream   Content           { get; init; } = Stream.Null;
    public string   OriginalFileName  { get; init; } = "";
    public string?  MimeType          { get; init; }
    /// <summary>Logical sub-folder under the storage base path. Default "flag-evidence".</summary>
    public string   SubfolderHint     { get; init; } = "flag-evidence";

    /// <summary>
    /// Slice 10 §7.4 — when set, the handler inserts a row in
    /// <c>TmX_Transaction_Document</c> after the document is persisted,
    /// linking the newly-uploaded file to this transaction. Null for the
    /// legacy flag-evidence path (where the transaction is resolved via
    /// the flag row, not the bridge).
    /// </summary>
    public int?     TransactionId     { get; init; }

    /// <summary>
    /// When true AND <see cref="TransactionId"/> is set, the file is stored under
    /// <c>Transaction/&lt;TransactionNumber&gt;/FlagsEvidence/</c> (vs the general
    /// <c>Transaction/&lt;TransactionNumber&gt;/</c> for ordinary transaction docs).
    /// </summary>
    public bool     IsFlagEvidence    { get; init; }
}

public class UploadDocumentResult
{
    public int     documentId          { get; set; }
    public string  originalFileName    { get; set; } = "";
    public string  storedFileName      { get; set; } = "";
    public string? mimeType            { get; set; }
    public long?   fileSizeBytes       { get; set; }
    public string? sha256Hash          { get; set; }
}
