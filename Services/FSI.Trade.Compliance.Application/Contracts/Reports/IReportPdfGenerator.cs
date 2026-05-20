namespace FSI.Trade.Compliance.Application.Contracts.Reports;

/// <summary>
/// Converts a rendered HTML report into PDF bytes. The legacy backend
/// shelled out to a Java <c>wkhtmltopdf</c> wrapper; the revamp uses
/// PuppeteerSharp (headless Chromium) — same input contract, better
/// fidelity for CSS Grid and modern table styles.
///
/// Page orientation is honoured: "L"/"Landscape" → landscape, anything
/// else → portrait. This matches the legacy <c>PageOrientation</c>
/// parameter the FE passes in the report request body.
/// </summary>
public interface IReportPdfGenerator
{
    Task<byte[]> GenerateAsync(string html, string? pageOrientation, CancellationToken ct = default);
}
