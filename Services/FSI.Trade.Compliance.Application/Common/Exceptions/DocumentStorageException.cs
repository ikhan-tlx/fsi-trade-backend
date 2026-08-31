namespace FSI.Trade.Compliance.Application.Common.Exceptions;

/// <summary>
/// A failure while reading or writing a document on the storage backend — e.g. the
/// storage directory is missing or the process identity lacks write permission
/// (a <see cref="System.UnauthorizedAccessException"/> / <see cref="System.IO.IOException"/>
/// at the filesystem level).
///
/// This is deliberately its OWN type so the API can map it to HTTP 500 (a server-side
/// fault) and log it — NOT to 401. The filesystem's <c>UnauthorizedAccessException</c>
/// would otherwise be caught by the generic 401 mapping and surface to the client as an
/// authentication challenge (login modal), masking a permissions/config problem.
/// </summary>
public class DocumentStorageException : Exception
{
    public string Code { get; }
    public DocumentStorageException(string? description = null, Exception? inner = null)
        : base(description ?? "The document could not be stored.", inner)
        => Code = "document_storage_unwritable";
}
