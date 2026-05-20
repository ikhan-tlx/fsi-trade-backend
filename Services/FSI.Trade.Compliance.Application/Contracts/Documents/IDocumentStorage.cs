using FSI.Trade.Compliance.Domain.Entities.Documents;

namespace FSI.Trade.Compliance.Application.Contracts.Documents;

/// <summary>
/// Writes / reads files for the generic <see cref="Document"/> store.
/// Implementations target a specific storage provider (local disk,
/// Azure Blob, S3); the catalogue's <c>Storage_Provider_Lkp_ID</c>
/// records which one was used per row so retrieval can route correctly
/// even when multiple providers coexist.
///
/// Lifecycle pattern — the caller stays in control of the DB:
///   1. Caller invokes <see cref="SaveAsync"/> → service writes the
///      file, returns a fully-populated <see cref="Document"/> with
///      hash, size, paths, provider FK set.
///   2. Caller adds the entity to <c>DbContext.Documents</c> and saves
///      changes inside its own unit of work.
///   3. If the caller rolls back, the on-disk file is orphaned — a
///      periodic cleanup job sweeps files without a matching Document
///      row. Worth the simplicity for a write-rarely path.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>
    /// Persists the stream to the underlying provider and returns a
    /// ready-to-save <see cref="Document"/> entity. Does NOT touch the
    /// database; the caller persists it.
    /// </summary>
    /// <param name="content">
    /// Source stream. May be non-seekable (e.g. ASP.NET form-file).
    /// Read once, streamed straight to disk, hashed in the same pass.
    /// </param>
    /// <param name="originalFileName">
    /// User-supplied filename. Sanitised by the implementation for
    /// path-traversal safety; used only on download for
    /// Content-Disposition. The on-disk filename is GUID-based.
    /// </param>
    /// <param name="mimeType">Optional content-type. Stored as-is.</param>
    /// <param name="uploadedBy">Attribution — typically the JWT user.</param>
    /// <param name="tenantId">Tenant the document belongs to.</param>
    /// <param name="subfolderHint">
    /// Logical grouping within the provider, e.g. "flag-evidence".
    /// Implementations append <c>/yyyy/MM/</c> so a single directory
    /// never grows past a month's uploads.
    /// </param>
    Task<Document> SaveAsync(
        Stream content,
        string originalFileName,
        string? mimeType,
        string uploadedBy,
        int tenantId,
        string subfolderHint,
        CancellationToken ct = default);

    /// <summary>
    /// Opens the backing file for read. Caller disposes the returned
    /// stream. Throws <see cref="FileNotFoundException"/> if the file
    /// can't be resolved (provider mismatch, missing on disk).
    /// </summary>
    Task<Stream> OpenReadAsync(Document document, CancellationToken ct = default);

    /// <summary>
    /// Best-effort hard-delete from storage. The <see cref="Document"/>
    /// row should be marked <c>ActiveFlag = false</c> by the caller for
    /// audit; this method removes the backing file. Returns false if the
    /// file was already gone.
    /// </summary>
    Task<bool> DeleteAsync(Document document, CancellationToken ct = default);
}
