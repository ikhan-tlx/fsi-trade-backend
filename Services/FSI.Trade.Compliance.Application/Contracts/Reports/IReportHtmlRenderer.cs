using System.Data;

namespace FSI.Trade.Compliance.Application.Contracts.Reports;

/// <summary>
/// Renders a report's HTML body by combining a Liquid template with a
/// <see cref="DataTable"/> result-set. The legacy backend used DotLiquid
/// for this — we keep the same templating language so existing
/// <c>TmX_Template.Template_Text</c> blobs render byte-for-byte the same.
///
/// Liquid sees a single root variable <c>rows</c> whose value is a list
/// of dictionaries (one per row, column name → value). Templates that
/// expected the legacy <c>{% for row in rows %}</c> loop continue to work
/// unchanged.
/// </summary>
public interface IReportHtmlRenderer
{
    string Render(string liquidTemplate, DataTable rows);
}
