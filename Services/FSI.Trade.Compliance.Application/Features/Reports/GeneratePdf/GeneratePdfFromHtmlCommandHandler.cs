using FSI.Trade.Compliance.Application.Contracts.Reports;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Reports.GeneratePdf;

public class GeneratePdfFromHtmlCommandHandler
    : IRequestHandler<GeneratePdfFromHtmlCommand, GeneratePdfFromHtmlResult>
{
    private readonly IReportPdfGenerator _pdf;

    public GeneratePdfFromHtmlCommandHandler(IReportPdfGenerator pdf) => _pdf = pdf;

    public async Task<GeneratePdfFromHtmlResult> Handle(
        GeneratePdfFromHtmlCommand req, CancellationToken ct)
    {
        var pdfBytes = await _pdf.GenerateAsync(req.HTML ?? string.Empty, req.PageOrientation, ct);

        return new GeneratePdfFromHtmlResult
        {
            PdfBytes          = pdfBytes,
            ReportVisibleName = req.ReportVisibleName
        };
    }
}
