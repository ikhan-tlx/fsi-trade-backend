using System.Data;
using ClosedXML.Excel;
using FSI.Trade.Compliance.Application.Contracts.Reports;

namespace FSI.Trade.Compliance.Infrastructure.Reports;

/// <summary>
/// ClosedXML implementation of <see cref="IReportExcelExporter"/>.
///
/// Sheet conventions:
///   • Sheet name = <c>reportVisibleName</c>, trimmed to Excel's 31-char
///     cap and stripped of the small set of characters Excel rejects
///     (<c>: \ / ? * [ ]</c>). Empty falls back to <c>"Report"</c>.
///   • Header row uses the DataTable column names, bolded, with a light
///     fill and a thin bottom border.
///   • Header row is frozen for scroll-stickiness.
///   • Columns auto-width after population. A header-only sheet still
///     adjusts so the header row is readable.
/// </summary>
internal class ClosedXmlReportExcelExporter : IReportExcelExporter
{
    private const int MaxSheetNameLength = 31;

    // Excel hard-caps cell text at 32,767 characters. ClosedXML throws an
    // ArgumentException ("Cells can hold a maximum of 32,767 characters")
    // before the workbook is even saved. SPs that return wide JSON blobs
    // (e.g. sp_customer_report's UDF_Data column) blow through this
    // routinely, so we truncate proactively. Leave room for an ellipsis
    // marker so the user notices the value was cut.
    private const int MaxCellTextLength      = 32_767;
    private const int MaxCellTextBeforeMark  = MaxCellTextLength - 3; // 3 chars for "..."
    private const string TruncationMarker    = "...";

    private static readonly char[] InvalidSheetChars = { ':', '\\', '/', '?', '*', '[', ']' };

    public byte[] Export(DataTable rows, string reportVisibleName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(BuildSheetName(reportVisibleName));

        if (rows.Columns.Count > 0)
        {
            // Header row.
            for (var c = 0; c < rows.Columns.Count; c++)
            {
                var headerCell = sheet.Cell(1, c + 1);
                headerCell.Value = rows.Columns[c].ColumnName;
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerCell.Style.Border.BottomBorder  = XLBorderStyleValues.Thin;
            }

            // Data rows.
            for (var r = 0; r < rows.Rows.Count; r++)
            {
                for (var c = 0; c < rows.Columns.Count; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    var raw  = rows.Rows[r][c];
                    SetCellValue(cell, raw);
                }
            }

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void SetCellValue(IXLCell cell, object? raw)
    {
        // Cells default to blank; nothing to do for null / DBNull.
        if (raw is null or DBNull) return;

        switch (raw)
        {
            case string s:       cell.Value = TruncateForCell(s); break;
            case bool b:         cell.Value = b;            break;
            case DateTime dt:    cell.Value = dt;           break;
            case DateOnly d:     cell.Value = d.ToDateTime(TimeOnly.MinValue); break;
            case TimeSpan ts:    cell.Value = ts;           break;
            case Guid g:         cell.Value = g.ToString(); break;
            case decimal m:      cell.Value = m;            break;
            case double dbl:     cell.Value = dbl;          break;
            case float f:        cell.Value = f;            break;
            case int i:          cell.Value = i;            break;
            case long l:         cell.Value = l;            break;
            case short sh:       cell.Value = sh;           break;
            case byte by:        cell.Value = by;           break;
            default:             cell.Value = TruncateForCell(raw.ToString() ?? string.Empty); break;
        }
    }

    /// <summary>
    /// Caps a string at Excel's per-cell limit (32,767). Values longer
    /// than that get sliced to <see cref="MaxCellTextBeforeMark"/> and
    /// have <see cref="TruncationMarker"/> appended so the user can see
    /// the value was truncated rather than silently corrupted. JSON
    /// blobs from SPs are the usual culprit (e.g. UDF_Data on
    /// TmX_Transaction_Detail can run tens of KB).
    /// </summary>
    private static string TruncateForCell(string value)
    {
        if (value.Length <= MaxCellTextLength) return value;
        return string.Concat(value.AsSpan(0, MaxCellTextBeforeMark), TruncationMarker);
    }

    private static string BuildSheetName(string? visibleName)
    {
        if (string.IsNullOrWhiteSpace(visibleName)) return "Report";
        var cleaned = new string(visibleName
            .Trim()
            .Where(ch => !InvalidSheetChars.Contains(ch))
            .ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) return "Report";
        return cleaned.Length <= MaxSheetNameLength
            ? cleaned
            : cleaned[..MaxSheetNameLength];
    }
}
