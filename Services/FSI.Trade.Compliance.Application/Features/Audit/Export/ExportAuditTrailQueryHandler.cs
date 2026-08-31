using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Audit;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Reports;
using FSI.Trade.Compliance.Application.Features.Audit.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Audit.Export;

/// <summary>
/// Builds the audit-trail export. Runs the same filtered/sorted query as the list
/// (capped), renders it as XLSX (ClosedXML) or a formatted PDF (Puppeteer) report,
/// stamps the report with an integrity statement (recomputed hash chain) + a filter
/// summary + generated-by/when, and records the export itself as an EXPORT audit event.
/// </summary>
public class ExportAuditTrailQueryHandler : IRequestHandler<ExportAuditTrailQuery, AuditExportResult>
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfContentType  = "application/pdf";

    private readonly IApplicationDbContext     _db;
    private readonly IReportExcelExporter      _excel;
    private readonly IReportPdfGenerator       _pdf;
    private readonly IAuditIntegrityVerifier   _integrity;
    private readonly IAuditWriter              _audit;
    private readonly ICurrentUserService       _user;

    public ExportAuditTrailQueryHandler(
        IApplicationDbContext db, IReportExcelExporter excel, IReportPdfGenerator pdf,
        IAuditIntegrityVerifier integrity, IAuditWriter audit, ICurrentUserService user)
    {
        _db = db; _excel = excel; _pdf = pdf; _integrity = integrity; _audit = audit; _user = user;
    }

    // PDF is a human-readable report (rendered by headless Chromium), so it is capped
    // well below the Excel cap — a giant PDF is slow to render and impractical to read.
    // Excel carries the full data set for bulk analysis.
    private const int PdfMaxRows = 5_000;

    public async Task<AuditExportResult> Handle(ExportAuditTrailQuery req, CancellationToken ct)
    {
        var format = (req.Format ?? "xlsx").Trim().ToLowerInvariant();
        var isPdf  = format is "pdf";
        var cap    = isPdf ? PdfMaxRows : ExportAuditTrailQuery.MaxRows;

        var q = AuditQueryBuilder.ApplyFilters(_db.AuditLogs.AsNoTracking(), req);
        q = AuditQueryBuilder.ApplySort(q, req.Sort);

        // Fetch one more than the cap so we can flag truncation.
        var raw = await q.Take(cap + 1)
            .Select(a => new Row
            {
                AuditId       = a.AuditId,
                CreatedDate   = a.CreatedDate,
                UserName      = a.UserName,
                UserId        = a.UserId,
                UserRole      = a.UserRole,
                OnBehalfOf    = a.OnBehalfOf,
                IpAddress     = a.IpAddress,
                Action        = a.Action,
                EntityType    = a.EntityType,
                EntityKey     = a.EntityKey,
                Outcome       = a.Outcome,
                StatusCode    = a.StatusCode,
                Screen        = a.Screen,
                HttpMethod    = a.HttpMethod,
                RequestPath   = a.RequestPath,
                Description   = a.Description,
                FailureReason = a.FailureReason,
                ChangesJson   = a.ChangesJson,
                SessionId     = a.SessionId,
                CorrelationId = a.CorrelationId,
                Host          = a.Host,
                Application   = a.Application,
                UserAgent     = a.UserAgent,
                RowHash       = a.RowHash,
                PrevHash      = a.PrevHash,
            })
            .ToListAsync(ct);

        var truncated = raw.Count > cap;
        if (truncated) raw = raw.Take(cap).ToList();

        // Full-chain integrity statement (verifies the WHOLE table, not just the filtered subset).
        var integrity = await _integrity.VerifyAsync(ct);

        var stamp     = DateTime.UtcNow;
        var fileStamp = stamp.ToString("yyyyMMdd-HHmmss");

        byte[] bytes = isPdf
            ? await _pdf.GenerateAsync(BuildHtml(raw, req, integrity, truncated, stamp), "L", ct)
            : _excel.Export(BuildTable(raw), "Audit Trail");

        // The export is itself an auditable event.
        var filterDesc = DescribeFilters(req);
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                Action      = "EXPORT",
                EntityType  = "AuditLog",
                Description  = $"Exported audit trail as {format.ToUpperInvariant()} — {raw.Count} row(s)"
                               + (truncated ? $" (capped at {cap:N0})" : "")
                               + (string.IsNullOrEmpty(filterDesc) ? "" : $"; filters: {filterDesc}"),
                Outcome     = "SUCCESS",
            }, ct);
        }
        catch { /* audit-writer is best-effort; never fail the export because logging failed */ }

        return new AuditExportResult
        {
            Content     = bytes,
            ContentType = isPdf ? PdfContentType : XlsxContentType,
            FileName    = $"audit-trail-{fileStamp}.{(isPdf ? "pdf" : "xlsx")}",
            RowCount    = raw.Count,
            Truncated   = truncated,
        };
    }

    // ---- Excel ----
    private static DataTable BuildTable(List<Row> rows)
    {
        var dt = new DataTable("Audit Trail");
        string[] cols =
        {
            "Audit Id", "When (UTC)", "User", "User Id", "Role", "On Behalf Of", "IP Address",
            "Action", "Entity", "Entity Key", "Outcome", "Status", "Screen", "Method", "Request Path",
            "Description", "Changes (old → new)", "Failure Reason", "Session Id", "Correlation Id",
            "Host", "Application", "User Agent", "Row Hash", "Prev Hash",
        };
        foreach (var c in cols) dt.Columns.Add(c, typeof(string));

        foreach (var r in rows)
        {
            dt.Rows.Add(
                r.AuditId.ToString(),
                Utc(r.CreatedDate),
                r.UserName ?? "", r.UserId ?? "", r.UserRole ?? "", r.OnBehalfOf ?? "", r.IpAddress ?? "",
                r.Action ?? "", r.EntityType ?? "", r.EntityKey ?? "", r.Outcome ?? "",
                r.StatusCode?.ToString() ?? "", r.Screen ?? "", r.HttpMethod ?? "", r.RequestPath ?? "",
                r.Description ?? "", FormatChanges(r.ChangesJson), r.FailureReason ?? "",
                r.SessionId ?? "", r.CorrelationId ?? "", r.Host ?? "", r.Application ?? "",
                r.UserAgent ?? "", r.RowHash ?? "", r.PrevHash ?? "");
        }
        return dt;
    }

    // ---- PDF (HTML → Puppeteer) ----
    private string BuildHtml(List<Row> rows, ExportAuditTrailQuery req, AuditIntegrityResult integrity, bool truncated, DateTime stamp)
    {
        var by         = _user.UserName ?? _user.UserId ?? "unknown";
        var filterDesc = DescribeFilters(req);
        var integrityCls = integrity.Intact ? "ok" : "fail";

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><style>");
        sb.Append(@"
            * { font-family: 'Segoe UI', Arial, sans-serif; }
            body { color: #1a1a2e; font-size: 10px; margin: 0; }
            h1 { font-size: 16px; margin: 0 0 2px; }
            .sub { color: #667085; font-size: 10px; margin: 0 0 10px; }
            .meta { margin: 0 0 8px; }
            .meta div { margin: 1px 0; }
            .banner { padding: 6px 10px; border-radius: 4px; margin: 8px 0; font-weight: 600; }
            .banner.ok  { background: #e7f6ec; color: #1a7f37; border: 1px solid #1a7f37; }
            .banner.fail{ background: #fdecec; color: #b3261e; border: 1px solid #b3261e; }
            table { border-collapse: collapse; width: 100%; margin-top: 6px; }
            th, td { border: 1px solid #d0d5dd; padding: 3px 5px; text-align: left; vertical-align: top; word-break: break-word; }
            th { background: #f2f4f7; font-size: 9px; text-transform: uppercase; letter-spacing: .03em; }
            td { font-size: 9px; }
            tr:nth-child(even) td { background: #fafafa; }
            .foot { margin-top: 10px; color: #667085; font-size: 9px; }
        ");
        sb.Append("</style></head><body>");
        sb.Append("<h1>Audit Trail Report</h1>");
        sb.Append("<p class='sub'>FSI Trade Compliance — tamper-evident activity log</p>");
        sb.Append("<div class='meta'>");
        sb.Append($"<div><b>Generated:</b> {E(Utc(stamp))} &nbsp;·&nbsp; <b>By:</b> {E(by)}</div>");
        sb.Append($"<div><b>Rows:</b> {rows.Count:N0}{(truncated ? $" (PDF capped at {PdfMaxRows:N0} — use the Excel export or narrow the date range for the full set)" : "")}</div>");
        sb.Append($"<div><b>Filters:</b> {E(string.IsNullOrEmpty(filterDesc) ? "none (all records)" : filterDesc)}</div>");
        sb.Append("</div>");
        sb.Append($"<div class='banner {integrityCls}'>{E(integrity.Summary)}</div>");

        sb.Append("<table><thead><tr>");
        foreach (var h in new[] { "When (UTC)", "User", "Role", "IP", "Action", "Entity", "Outcome", "Screen", "Description" })
            sb.Append($"<th>{h}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in rows)
        {
            var entity = r.EntityType + (string.IsNullOrEmpty(r.EntityKey) ? "" : $" #{r.EntityKey}");
            var desc   = r.Description ?? "";
            if (!string.IsNullOrEmpty(r.FailureReason)) desc += $" — {r.FailureReason}";
            sb.Append("<tr>");
            sb.Append($"<td>{E(Utc(r.CreatedDate))}</td>");
            sb.Append($"<td>{E(r.UserName ?? r.UserId ?? "")}</td>");
            sb.Append($"<td>{E(r.UserRole ?? "")}</td>");
            sb.Append($"<td>{E(r.IpAddress ?? "")}</td>");
            sb.Append($"<td>{E(r.Action ?? "")}</td>");
            sb.Append($"<td>{E(entity)}</td>");
            sb.Append($"<td>{E(r.Outcome ?? "")}</td>");
            sb.Append($"<td>{E(r.Screen ?? "")}</td>");
            sb.Append($"<td>{E(desc)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append("<p class='foot'>Timestamps are UTC. Full technical detail (session, correlation, user-agent, field-level before/after, row + previous hash) is available in the Excel export. Integrity is verified by recomputing the SHA-256 hash chain across the entire audit log.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string DescribeFilters(ExportAuditTrailQuery r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Filter))     parts.Add($"text=\"{r.Filter.Trim()}\"");
        if (r.DateFrom.HasValue)                       parts.Add($"from={r.DateFrom.Value:yyyy-MM-dd}");
        if (r.DateTo.HasValue)                         parts.Add($"to={r.DateTo.Value:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(r.UserName))    parts.Add($"user={r.UserName.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.EntityType))  parts.Add($"entity={r.EntityType.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Action))      parts.Add($"action={r.Action.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Outcome))     parts.Add($"outcome={r.Outcome.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Screen))      parts.Add($"screen={r.Screen.Trim()}");
        return string.Join(", ", parts);
    }

    private static string FormatChanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var parts = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var field = el.TryGetProperty("field", out var f) ? f.GetString() : null;
                var oldV  = el.TryGetProperty("old", out var o) ? o.ToString() : "";
                var newV  = el.TryGetProperty("new", out var n) ? n.ToString() : "";
                parts.Add($"{field}: {oldV} → {newV}");
            }
            return string.Join("; ", parts);
        }
        catch { return json; }
    }

    private static string Utc(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private sealed class Row
    {
        public long AuditId { get; set; }
        public DateTime CreatedDate { get; set; }
        public int? StatusCode { get; set; }
        public string? UserName { get; set; }
        public string? UserId { get; set; }
        public string? UserRole { get; set; }
        public string? OnBehalfOf { get; set; }
        public string? IpAddress { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public string? EntityKey { get; set; }
        public string? Outcome { get; set; }
        public string? Screen { get; set; }
        public string? HttpMethod { get; set; }
        public string? RequestPath { get; set; }
        public string? Description { get; set; }
        public string? FailureReason { get; set; }
        public string? ChangesJson { get; set; }
        public string? SessionId { get; set; }
        public string? CorrelationId { get; set; }
        public string? Host { get; set; }
        public string? Application { get; set; }
        public string? UserAgent { get; set; }
        public string? RowHash { get; set; }
        public string? PrevHash { get; set; }
    }
}
