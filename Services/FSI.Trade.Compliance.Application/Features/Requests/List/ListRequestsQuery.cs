using System.Text.Json.Serialization;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Requests.List;

/// <summary>
/// Paged maker/checker request list over <c>TmX_Request</c>. Backs the FE
/// Requests page grid (<c>GET /api/v1/Request?page=&amp;pageSize=&amp;sort=</c>).
/// Slice 14.
///
/// The FE resolves Request-Type / Entity-Status / Request-Status NAMES
/// client-side from its constants map (it only needs the CODES), so this query
/// returns the raw codes plus creator/date. DTO keys are pinned to PascalCase
/// with [JsonPropertyName] because the Requests page reads RequestId /
/// RequestTypeCode / CreatedDate etc. (the API otherwise serialises camelCase).
/// </summary>
public class ListRequestsQuery : PagedQuery, IRequest<PagedResult<RequestListItemDto>>
{
}

public class RequestListItemDto
{
    [JsonPropertyName("RequestId")]         public long      RequestId         { get; set; }
    [JsonPropertyName("RequestTypeCode")]   public string?   RequestTypeCode   { get; set; }
    [JsonPropertyName("EntityStatusCode")]  public string?   EntityStatusCode  { get; set; }
    [JsonPropertyName("RequestStatusCode")] public string?   RequestStatusCode { get; set; }
    [JsonPropertyName("EntityId")]          public string?   EntityId          { get; set; }
    [JsonPropertyName("CreatedBy")]         public string?   CreatedBy         { get; set; }
    [JsonPropertyName("CreatedDate")]       public DateTime  CreatedDate       { get; set; }
    // No Executed_By column on TmX_Request yet — surfaced as null so the FE
    // column renders "-". Add the column + mapping when the approval flow lands.
    [JsonPropertyName("ExecutedBy")]        public string?   ExecutedBy        { get; set; }
}

public class ListRequestsQueryHandler
    : IRequestHandler<ListRequestsQuery, PagedResult<RequestListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<RequestListItemDto>> Handle(ListRequestsQuery req, CancellationToken ct)
    {
        var q = _db.Requests.AsNoTracking();

        // Sort: FE sends "<Field>-<asc|desc>" where Field is one of the column
        // names below. Default newest-first.
        var (field, desc) = ParseSort(req.Sort);
        q = (field, desc) switch
        {
            ("RequestType",   false) => q.OrderBy(r => r.RequestTypeCode),
            ("RequestType",   true)  => q.OrderByDescending(r => r.RequestTypeCode),
            ("EntityStatus",  false) => q.OrderBy(r => r.EntityStatusCode),
            ("EntityStatus",  true)  => q.OrderByDescending(r => r.EntityStatusCode),
            ("RequestStatus", false) => q.OrderBy(r => r.RequestStatusCode),
            ("RequestStatus", true)  => q.OrderByDescending(r => r.RequestStatusCode),
            ("CreatedBy",     false) => q.OrderBy(r => r.CreatedBy),
            ("CreatedBy",     true)  => q.OrderByDescending(r => r.CreatedBy),
            ("CreatedDate",   false) => q.OrderBy(r => r.CreatedDate),
            ("ExecutedBy",    false) => q.OrderBy(r => r.LastUpdatedBy),
            ("ExecutedBy",    true)  => q.OrderByDescending(r => r.LastUpdatedBy),
            _                        => q.OrderByDescending(r => r.CreatedDate),
        };

        var total = await q.CountAsync(ct);

        var rows = await q
            .Skip(req.Skip)
            .Take(req.Take)
            .Select(r => new RequestListItemDto
            {
                RequestId         = r.RequestId,
                RequestTypeCode   = r.RequestTypeCode,
                EntityStatusCode  = r.EntityStatusCode,
                RequestStatusCode = r.RequestStatusCode,
                EntityId          = r.EntityId,
                CreatedBy         = r.CreatedBy,
                CreatedDate       = r.CreatedDate,
                ExecutedBy        = r.LastUpdatedBy
            })
            .ToListAsync(ct);

        return new PagedResult<RequestListItemDto>
        {
            items    = rows,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    // "<field>-<asc|desc>"; first token only. Defaults to CreatedDate desc.
    private static (string Field, bool Desc) ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return ("CreatedDate", true);
        var first = sort.Split(',')[0];
        var dash  = first.LastIndexOf('-');
        if (dash <= 0) return (first.Trim(), true);
        var field = first[..dash].Trim();
        var dir   = first[(dash + 1)..].Trim();
        return (field, dir.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}
