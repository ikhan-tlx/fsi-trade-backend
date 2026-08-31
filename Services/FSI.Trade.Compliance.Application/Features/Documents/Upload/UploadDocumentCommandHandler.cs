using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Documents;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

        // Resolve the storage folder.
        //   • Transaction-scoped upload → Transaction/<TransactionNumber>/... so all of a
        //     transaction's files live together (flag evidence under a FlagsEvidence
        //     sub-folder). The transaction is already a saved draft, so it always has a
        //     number; if it were somehow missing we fall back to the id to stay safe.
        //   • No transaction (generic/legacy) → keep the loose hint + /yyyy/MM/ partition.
        string subfolder;
        bool   appendDatePartition;
        if (req.TransactionId.HasValue)
        {
            var txnNumber = await _db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == req.TransactionId.Value)
                .Select(t => t.TransactionNumber)
                .FirstOrDefaultAsync(ct);

            var folderKey = string.IsNullOrWhiteSpace(txnNumber)
                ? req.TransactionId.Value.ToString()
                : txnNumber!;

            subfolder = req.IsFlagEvidence
                ? $"Transaction/{folderKey}/FlagsEvidence"
                : $"Transaction/{folderKey}";
            appendDatePartition = false;
        }
        else
        {
            subfolder = req.SubfolderHint;
            appendDatePartition = true;
        }

        // Write the file to backing storage. Returns a fully-populated
        // Document entity (size + hash + paths + provider all set).
        var document = await _storage.SaveAsync(
            content:             req.Content,
            originalFileName:    req.OriginalFileName,
            mimeType:            req.MimeType,
            uploadedBy:          userId,
            tenantId:            tenantId,
            subfolderHint:       subfolder,
            appendDatePartition: appendDatePartition,
            ct:                  ct);

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        // Slice 10 §7.4 — optional bridge insert. Saved separately so the
        // document gets its identity-generated ID first; the bridge row
        // then references it. Idempotent at the row level via the unique
        // (Transaction_ID, Document_ID) constraint — a re-upload of an
        // already-attached document would throw on the second commit
        // here. That's the intended behaviour; the controller doesn't
        // currently surface "replace existing" semantics.
        if (req.TransactionId.HasValue)
        {
            _db.TransactionDocuments.Add(new TransactionDocument
            {
                TransactionId = req.TransactionId.Value,
                DocumentId    = document.DocumentId,
                TenantId      = tenantId,
                ActiveFlag    = true,
                CreatedBy     = userId,
                CreatedDate   = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }

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
