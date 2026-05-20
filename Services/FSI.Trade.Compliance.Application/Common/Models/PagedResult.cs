namespace FSI.Trade.Compliance.Application.Common.Models;

/// <summary>
/// Standard paged-list payload. Goes inside <c>data</c> on any
/// <c>GET /api/v1/...?page=&amp;pageSize=&amp;sort=</c> endpoint.
/// </summary>
public class PagedResult<T>
{
    public List<T> items    { get; set; } = new();
    public int     total    { get; set; }
    public int     page     { get; set; }
    public int     pageSize { get; set; }
}

/// <summary>
/// Common Kendo-compatible query-string params accepted by every paged
/// endpoint. The FE sends both <c>page/pageSize</c> AND <c>take/skip</c>;
/// we ignore the redundant pair and read <c>page/pageSize</c>.
///
/// Sort syntax: "<field>-<asc|desc>"  e.g.  "createdDate-desc".
/// Multiple sorts: comma-separated. Today only the first is honoured.
/// </summary>
public class PagedQuery
{
    public int     Page     { get; set; } = 1;
    public int     PageSize { get; set; } = 10;
    public string? Sort     { get; set; }
    public string? Filter   { get; set; }

    public int Skip => (Math.Max(1, Page) - 1) * Math.Max(1, PageSize);
    public int Take => Math.Clamp(PageSize, 1, 1000);
}
