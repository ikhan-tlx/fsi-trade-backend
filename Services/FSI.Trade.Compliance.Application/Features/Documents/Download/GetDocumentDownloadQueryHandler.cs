using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Documents;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Documents.Download;

public class GetDocumentDownloadQueryHandler
    : IRequestHandler<GetDocumentDownloadQuery, DocumentDownloadResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorage      _storage;

    public GetDocumentDownloadQueryHandler(
        IApplicationDbContext db,
        IDocumentStorage      storage)
    {
        _db      = db;
        _storage = storage;
    }

    public async Task<DocumentDownloadResult> Handle(
        GetDocumentDownloadQuery req, CancellationToken ct)
    {
        var document = await _db.Documents.AsNoTracking()
            .Where(d => d.DocumentId == req.DocumentId && d.ActiveFlag)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("document_not_found",
                $"Document {req.DocumentId} not found or inactive.");

        var stream = await _storage.OpenReadAsync(document, ct);

        return new DocumentDownloadResult
        {
            Content          = stream,
            OriginalFileName = document.OriginalFileName,
            MimeType         = document.MimeType,
        };
    }
}
