using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Documents.Download;

/// <summary>
/// Resolves a <c>TmX_Document</c> row and opens the backing file for
/// read. The controller wraps the returned stream in a
/// <c>FileStreamResult</c>.
///
/// Maps to <c>GET /api/v1/Document/{id}</c>.
/// </summary>
public record GetDocumentDownloadQuery(int DocumentId)
    : IRequest<DocumentDownloadResult>;

public class DocumentDownloadResult
{
    /// <summary>Open read stream. Caller disposes.</summary>
    public required Stream  Content          { get; init; }
    public required string  OriginalFileName { get; init; }
    public          string? MimeType         { get; init; }
}
