using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Reports.GeneratePdf;
using FSI.Trade.Compliance.Application.Features.Reports.RunExcel;
using FSI.Trade.Compliance.Application.Features.Reports.RunHtml;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Reports surface. Slice 7. Three endpoints, three verbs — the verbs
/// match legacy AngularJS ReportService so the existing FE works without
/// a single line of change (path + method + body):
///
///   POST   /api/v1/Report/ReportHTML            → JSON envelope { html, ... }
///   PUT    /api/v1/Report/GeneratePdfFromHtml   → binary PDF
///   PUT    /api/v1/Report/ReportExcel           → binary XLSX
///
/// Why PUT for the two binary endpoints? Legacy quirk — see
/// <c>businessReportsApi.downloadReport</c> in the FE. Keeping the verb
/// avoids breaking the FE for the small win of "REST conformance".
///
/// Allowlist enforcement happens inside the handlers (against TmX_Lookup
/// REPORT_TYPE rows); the controller itself is unaware of SP names.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReportController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Runs the report's SP, renders the matching Liquid template, and
    /// returns the rendered HTML wrapped in the standard envelope.
    /// </summary>
    [HttpPost("ReportHTML")]
    public async Task<IActionResult> ReportHtml(
        [FromBody] RunReportHtmlCommand body,
        CancellationToken ct)
    {
        var result = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<RunReportHtmlResult>.Ok(result));
    }

    /// <summary>
    /// Converts pre-rendered HTML into PDF bytes. Stream as
    /// <c>application/pdf</c> with a download filename derived from the
    /// supplied <c>ReportVisibleName</c>.
    /// </summary>
    [HttpPut("GeneratePdfFromHtml")]
    public async Task<IActionResult> GeneratePdfFromHtml(
        [FromBody] GeneratePdfFromHtmlCommand body,
        CancellationToken ct)
    {
        var result   = await _mediator.Send(body, ct);
        var filename = BuildFilename(result.ReportVisibleName, ".pdf");
        return File(result.PdfBytes, "application/pdf", filename);
    }

    /// <summary>
    /// Runs the report's SP and streams the result-set as a single-sheet
    /// XLSX. No template involved — Excel shows the rows as-is.
    /// </summary>
    [HttpPut("ReportExcel")]
    public async Task<IActionResult> ReportExcel(
        [FromBody] RunReportExcelCommand body,
        CancellationToken ct)
    {
        var result   = await _mediator.Send(body, ct);
        var filename = BuildFilename(result.ReportVisibleName, ".xlsx");
        return File(
            result.ExcelBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    private static string BuildFilename(string? visibleName, string extension)
    {
        var name = string.IsNullOrWhiteSpace(visibleName) ? "report" : visibleName.Trim();

        // Strip a small set of characters that browsers/users dislike in
        // download filenames. We don't try to be exhaustive — just remove
        // path separators and the obvious shell-unsafe ones.
        foreach (var ch in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
            name = name.Replace(ch.ToString(), "");

        return $"{name}-{DateTime.UtcNow:yyyy-MM-dd}{extension}";
    }
}
