using FSI.Trade.Compliance.Application.Features.Reports.Common;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Reports.RunHtml;

/// <summary>
/// Slice 7 — POST /api/v1/Report/ReportHTML.
///
/// Pipeline:
///   1. Allowlist-check the StoredProcedure against TmX_Lookup REPORT_TYPE rows.
///   2. Load the TmX_Template row matching <see cref="ReportRequestDto.ReportName"/>
///      (404 if missing — admin hasn't configured a body for this report).
///   3. Execute the SP with the supplied Arguments via IStoredProcedureRunner.
///   4. Render the template via IReportHtmlRenderer with the SP result-set
///      exposed as <c>rows</c>.
///   5. Return the rendered HTML alongside the visible report name, so
///      the FE can either display it or POST it back to the PDF endpoint.
/// </summary>
public class RunReportHtmlCommand : ReportRequestDto, IRequest<RunReportHtmlResult>
{
}

public class RunReportHtmlResult
{
    public string  html              { get; set; } = "";
    public string? reportName        { get; set; }
    public string? reportVisibleName { get; set; }
    public int     rowCount          { get; set; }
}
