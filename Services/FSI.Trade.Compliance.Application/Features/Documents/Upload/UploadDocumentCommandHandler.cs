using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Documents;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Documents.Upload;

public class UploadDocumentCommandHandler
    : IRequestHandler<UploadDocumentCommand, UploadDocumentResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorage      _storage;
    private readonly ICurrentUserService   _current;

    public UploadDocumentCommandHandler(
        IApplicationDbContext db,
        IDocumentStorage      storage,
        ICurrentUserService   current)
    {
        _db      = db;
        _storage = storage;
        _current = current;
    }

    public async Task<UploadDocumentResult> Handle(
        UploadDocumentCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Document upload requires an authenticated caller.");

        var tenantId = _current.TenantId ?? 1;

        // Write the file to backing storage. Returns a fully-populated
        // Document entity (size + hash + paths + provider all set).
        var document = await _storage.SaveAsync(
            content:           req.Content,
            originalFileName:  req.OriginalFileName,
            mimeType:          req.MimeType,
            uploadedBy:        userId,
            tenantId:          tenantId,
            subfolderHint:     req.SubfolderHint,
            ct:                ct);

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        return new UploadDocumentResult
        {
            documentId       = document.DocumentId,
            originalFileName = document.OriginalFileName,
            storedFileName   = document.StoredFileName,
            mimeType         = document.MimeType,
            fileSizeBytes    = document.FileSizeBytes,
            sha256Hash       = document.Sha256Hash,
        };
    }
}
