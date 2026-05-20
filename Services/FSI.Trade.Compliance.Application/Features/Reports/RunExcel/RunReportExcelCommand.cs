using FSI.Trade.Compliance.Application.Features.Reports.Common;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Reports.RunExcel;

/// <summary>
/// Slice 7 — PUT /api/v1/Report/ReportExcel.
///
/// Runs the report's stored procedure fresh (no Liquid template needed —
/// XLSX shows raw rows) and streams the result as a single-sheet XLSX.
///
/// Verb is PUT to match the legacy AngularJS ReportService — see
/// <see cref="GeneratePdf.GeneratePdfFromHtmlCommand"/> for why we keep
/// the legacy verb even though it's unusual.
/// </summary>
public class RunReportExcelCommand : ReportRequestDto, IRequest<RunReportExcelResult>
{
}

public class RunReportExcelResult
{
    public byte[] ExcelBytes        { get; set; } = Array.Empty<byte>();
    public string? ReportVisibleName { get; set; }
    public int     RowCount          { get; set; }
}
