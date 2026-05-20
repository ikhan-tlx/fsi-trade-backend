using System.Data;

namespace FSI.Trade.Compliance.Application.Contracts.Reports;

/// <summary>
/// Streams a single-sheet XLSX from a <see cref="DataTable"/>. The
/// legacy backend used EPPlus + a hand-rolled style block; the revamp
/// uses ClosedXML which gives us banded styling, auto-width, and a
/// frozen header row out of the box.
///
/// The implementation is responsible for picking a sensible sheet name
/// (the report's visible name, truncated to Excel's 31-char limit).
/// </summary>
public interface IReportExcelExporter
{
    byte[] Export(DataTable rows, string reportVisibleName);
}
