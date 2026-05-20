using System.Data;
using DotLiquid;
using FSI.Trade.Compliance.Application.Contracts.Reports;

namespace FSI.Trade.Compliance.Infrastructure.Reports;

/// <summary>
/// DotLiquid implementation of <see cref="IReportHtmlRenderer"/>.
///
/// Maps the SP result-set into a list of <see cref="Hash"/> entries —
/// DotLiquid's preferred model shape — and exposes them on the root
/// scope as <c>rows</c>, matching the legacy template contract so
/// existing <c>{% for row in rows %}</c> loops continue to render.
///
/// Naming hygiene:
///   • DotLiquid is case-sensitive and rejects '.' in keys. SQL column
///     names like <c>Customer.Name</c> are sanitised to <c>Customer_Name</c>.
///   • Both the original and the sanitised key are exposed so a template
///     written against the legacy server still picks up its values.
/// </summary>
internal class DotLiquidReportHtmlRenderer : IReportHtmlRenderer
{
    public string Render(string liquidTemplate, DataTable rows)
    {
        if (string.IsNullOrWhiteSpace(liquidTemplate))
            return string.Empty;

        var rowHashes = new List<Hash>(rows.Rows.Count);
        foreach (DataRow row in rows.Rows)
        {
            var hash = new Hash();
            foreach (DataColumn col in rows.Columns)
            {
                var raw = row[col];
                var value = raw is DBNull ? null : raw;
                var name  = col.ColumnName ?? string.Empty;

                hash[name] = value;
                if (name.Contains('.'))
                    hash[name.Replace('.', '_')] = value;
            }
            rowHashes.Add(hash);
        }

        var template = Template.Parse(liquidTemplate);
        return template.Render(Hash.FromAnonymousObject(new
        {
            rows  = rowHashes,
            count = rowHashes.Count,
        }));
    }
}
