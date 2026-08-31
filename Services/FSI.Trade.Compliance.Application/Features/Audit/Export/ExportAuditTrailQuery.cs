using FSI.Trade.Compliance.Application.Features.Audit.Common;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Audit.Export;

/// <summary>
/// Export the audit trail as a downloadable report. Carries the SAME filter set as
/// the paged listing (so the report matches what the operator sees) plus the output
/// <see cref="Format"/> ("xlsx" or "pdf"). Paging is intentionally ignored — the
/// export returns every row matching the filter, capped at <see cref="MaxRows"/>.
/// GET /api/v1/Audit/Export?format=&amp;sort=&amp;filter=&amp;dateFrom=&amp;…
/// </summary>
public class ExportAuditTrailQuery : IRequest<AuditExportResult>, IAuditFilterCriteria
{
    public const int MaxRows = 100_000;

    public string  Format     { get; set; } = "xlsx";   // xlsx | pdf
    public string? Sort       { get; set; }

    public string? Filter     { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo   { get; set; }
    public string? UserName   { get; set; }
    public string? EntityType { get; set; }
    public string? Action     { get; set; }
    public string? Outcome    { get; set; }
    public string? Screen     { get; set; }
}

public sealed class AuditExportResult
{
    public byte[] Content     { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName    { get; set; } = "audit-trail";
    public int    RowCount    { get; set; }
    public bool   Truncated   { get; set; }
}
