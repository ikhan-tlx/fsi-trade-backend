using FSI.Trade.Compliance.Application.Features.Reports.Common;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Reports.GeneratePdf;

/// <summary>
/// Slice 7 — PUT /api/v1/Report/GeneratePdfFromHtml.
///
/// Converts pre-rendered HTML (typically produced by the
/// /Report/ReportHTML endpoint) into PDF bytes via PuppeteerSharp.
///
/// Verb is PUT to match the legacy AngularJS ReportService — the backend
/// dispatcher keyed off the verb. Changing it to POST would break any
/// FE call sites that haven't been migrated.
/// </summary>
public class GeneratePdfFromHtmlCommand : ReportRequestDto, IRequest<GeneratePdfFromHtmlResult>
{
}

public class GeneratePdfFromHtmlResult
{
    public byte[] PdfBytes          { get; set; } = Array.Empty<byte>();
    public string? ReportVisibleName { get; set; }
}
