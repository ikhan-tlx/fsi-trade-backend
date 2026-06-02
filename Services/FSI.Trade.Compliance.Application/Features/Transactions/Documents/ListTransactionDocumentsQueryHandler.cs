using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Documents;

/// <summary>
/// Lists documents linked to a transaction via the
/// <c>TmX_Transaction_Document</c> bridge. Active rows only — soft-
/// deactivated linkages and soft-deactivated documents are both hidden.
///
/// Flag-evidence documents are NOT returned here: they're linked via
/// <c>TmX_Transaction_Flag.Evidence_Document_ID</c> rather than the
/// transaction-document bridge, and surface on the Flags panel.
///
/// Uploader display names are batch-resolved against TmX_User in a single
/// IN-list query.
/// </summary>
public class ListTransactionDocumentsQueryHandler
    : IRequestHandler<ListTransactionDocumentsQuery, IReadOnlyList<TransactionDocumentDto>>
{
    private readonly IApplicationDbContext _db;

    public ListTransactionDocumentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransactionDocumentDto>> Handle(
        ListTransactionDocumentsQuery req,
        CancellationToken             ct)
    {
        // 404 on bad ids rather than silently returning [].
        var exists = await _db.Transactions.AsNoTracking()
            .AnyAsync(t => t.TransactionId == req.TransactionId, ct);
        if (!exists)
            throw new NotFoundException(
                "transaction_not_found",
                $"Transaction {req.TransactionId} does not exist.");

        // Join bridge → document. Order by the bridge insert timestamp
        // (which IS the "attached to this transaction at" timestamp; the
        // document's UploadedDate is when the file landed in storage —
        // for shared docs that pre-date the attachment, those diverge).
        var rows = await (
            from link in _db.TransactionDocuments.AsNoTracking()
            join doc  in _db.Documents.AsNoTracking() on link.DocumentId equals doc.DocumentId
            where link.TransactionId == req.TransactionId
              &&  link.ActiveFlag
              &&  doc.ActiveFlag
            orderby link.CreatedDate descending
            select new
            {
                doc.DocumentId,
                doc.OriginalFileName,
                doc.MimeType,
                doc.FileSizeBytes,
                doc.UploadedBy,
                AttachedDate = link.CreatedDate
            }).ToListAsync(ct);

        if (rows.Count == 0)
            return Array.Empty<TransactionDocumentDto>();

        // Batch-resolve uploader display names.
        var uploaderIds = rows
            .Select(d => d.UploadedBy)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        var nameById = uploaderIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (await _db.Users.AsNoTracking()
                .Where(u => uploaderIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
                .ToListAsync(ct))
                .ToDictionary(
                    u => u.Id,
                    u => Compose(u.FirstName, u.LastName, u.UserName),
                    StringComparer.OrdinalIgnoreCase);

        return rows.Select(d => new TransactionDocumentDto
        {
            documentId       = d.DocumentId,
            originalFileName = d.OriginalFileName,
            mimeType         = d.MimeType,
            fileSizeBytes    = d.FileSizeBytes,
            uploadedBy       = d.UploadedBy,
            uploadedByName   = nameById.TryGetValue(d.UploadedBy, out var nm) ? nm : null,
            uploadedDate     = d.AttachedDate,
            downloadUrl      = $"/api/v1/Document/{d.DocumentId}"
        }).ToList();
    }

    private static string Compose(string? firstName, string? lastName, string? userName)
    {
        var first = (firstName ?? "").Trim();
        var last  = (lastName  ?? "").Trim();
        if (first.Length > 0 || last.Length > 0)
            return (first + " " + last).Trim();
        return userName ?? "";
    }
}
