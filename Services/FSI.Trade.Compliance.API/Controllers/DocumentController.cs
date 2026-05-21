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
    /// Multipart upload. Single file per request; multipart form-field
    /// name is <c>"file"</c>. Optional <c>subfolderHint</c> on the
    /// query string (e.g. <c>POST /Document?subfolderHint=customer-docs</c>).
    /// Defaults to "flag-evidence".
    ///
    /// <para>
    /// HIDDEN FROM SWAGGER. Swashbuckle 6.8.x has an over-eager check
    /// that throws on any IFormFile parameter when other params (even
    /// <c>[FromQuery]</c> or <c>CancellationToken</c>) are present —
    /// no documented workaround other than downgrading the package or
    /// removing the action from the OpenAPI doc. We picked the latter.
    /// The endpoint is fully functional; it just isn't in the Swagger UI.
    /// </para>
    ///
    /// <para>
    /// Test directly with curl:
    /// <code>
    /// curl -X POST "https://localhost:5081/api/v1/Document?subfolderHint=flag-evidence" \
    ///      -H "Authorization: Bearer &lt;jwt&gt;" \
    ///      -F "file=@C:\path\to\file.pdf"
    /// </code>
    /// </para>
    ///
    /// <para>
    /// TODO: once Swashbuckle ships a fix (or we add a custom OperationFilter
    /// that generates the multipart schema), drop the
    /// <c>[ApiExplorerSettings]</c> attribute and the endpoint reappears
    /// in Swagger.
    /// </para>
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(50_000_000)]   // 50MB cap per upload
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Upload(
        IFormFile           file,
        [FromQuery] string? subfolderHint,
        CancellationToken   ct)
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

