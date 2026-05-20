using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Documents.Download;
using FSI.Trade.Compliance.Application.Features.Documents.Upload;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Slice 8 — generic document upload + download. Today drives flag
/// evidence; same endpoints serve any future attachment use case
/// (customer documents, audit attachments, etc.) via the same
/// <c>TmX_Document</c> store. Caller distinguishes the use by
/// <c>subfolderHint</c> on upload.
///
///   POST /api/v1/Document        multipart upload — returns metadata
///   GET  /api/v1/Document/{id}   binary stream     — file download
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class DocumentController : ControllerBase
{
    private readonly IMediator _mediator;
    public DocumentController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Multipart upload. Single file per request; multipart name is
    /// "file". Optional form field "subfolderHint" overrides the
    /// default of "flag-evidence" — e.g. set to "customer-docs"
    /// for non-flag uploads.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(50_000_000)]   // 50MB cap per upload
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string?   subfolderHint,
        CancellationToken    ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ResponseViewModel<object>.Fail(
                400, "file_required", "Upload requires a non-empty file field."));

        await using var stream = file.OpenReadStream();

        var result = await _mediator.Send(new UploadDocumentCommand
        {
            Content           = stream,
            OriginalFileName  = file.FileName,
            MimeType          = string.IsNullOrWhiteSpace(file.ContentType)
                                    ? null
                                    : file.ContentType,
            SubfolderHint     = string.IsNullOrWhiteSpace(subfolderHint)
                                    ? "flag-evidence"
                                    : subfolderHint!,
        }, ct);

        return Ok(ResponseViewModel<UploadDocumentResult>.Ok(result));
    }

    /// <summary>
    /// Streams the file back. Content-Disposition forces a browser
    /// download with the original filename; Mime-Type honours what was
    /// captured on upload, falling back to application/octet-stream.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDocumentDownloadQuery(id), ct);

        // The FileStreamResult takes ownership of the stream and
        // disposes it once the response is flushed.
        return File(
            result.Content,
            result.MimeType ?? "application/octet-stream",
            result.OriginalFileName);
    }
}
