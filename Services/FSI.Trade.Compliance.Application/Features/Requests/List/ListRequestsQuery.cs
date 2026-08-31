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
    // Flat filter params (bound from the query string by [FromQuery]). The FE
    // sends only the ones that are set. eq match for the coded columns; a
    // case-insensitive contains for the free-text people columns; exact day
    // match for the submitted date. (We use flat params rather than a nested
    // Kendo `filter` object so the query string serialises cleanly.)
    public string? RequestTypeCode   { get; set; }
    public string? EntityStatusCode  { get; set; }
    public string? RequestStatusCode { get; set; }
    public string? CreatedBy         { get; set; }
    public string? ExecutedBy        { get; set; }
    public string? CreatedDate       { get; set; }  // yyyy-MM-dd
}

public class RequestListItemDto
{
    [JsonPropertyName("RequestId")]         public long      RequestId         { get; set; }
    [JsonPropertyName("RequestTypeCode")]   public string?   RequestTypeCode   { get; set; }
    [JsonPropertyName("EntityStatusCode")]  public string?   EntityStatusCode  { get; set; }
    [JsonPropertyName("RequestStatusCode")] public string?   RequestStatusCode { get; set; }
    [JsonPropertyName("EntityId")]          public string?   EntityId          { get; set; }
    // Human name of the entity being changed, parsed from Entity_Json so the
    // grid's Entity column has something to show (the row stores only ids).
    [JsonPropertyName("EntityName")]        public string?   EntityName        { get; set; }
    [JsonPropertyName("CreatedBy")]         public string?   CreatedBy         { get; set; }
    [JsonPropertyName("CreatedDate")]       public DateTime  CreatedDate       { get; set; }
    [JsonPropertyName("ExecutedBy")]        public string?   ExecutedBy        { get; set; }
    // The checker's decision date (Last_Updated_Date) — null while pending.
    [JsonPropertyName("ExecutedDate")]      public DateTime? ExecutedDate      { get; set; }
}

public class ListRequestsQueryHandler
    : IRequestHandler<ListRequestsQuery, PagedResult<RequestListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<RequestListItemDto>> Handle(ListRequestsQuery req, CancellationToken ct)
    {
        var q = _db.Requests.AsNoTracking();

        // Filters (applied before count/sort so the total reflects the filter).
        if (!string.IsNullOrWhiteSpace(req.RequestTypeCode))
            q = q.Where(r => r.RequestTypeCode == req.RequestTypeCode);
        if (!string.IsNullOrWhiteSpace(req.EntityStatusCode))
            q = q.Where(r => r.EntityStatusCode == req.EntityStatusCode);
        if (!string.IsNullOrWhiteSpace(req.RequestStatusCode))
            q = q.Where(r => r.RequestStatusCode == req.RequestStatusCode);
        if (!string.IsNullOrWhiteSpace(req.CreatedBy))
        {
            var t = req.CreatedBy.Trim();
            q = q.Where(r => r.CreatedBy != null && EF.Functions.Like(r.CreatedBy, $"%{t}%"));
        }
        if (!string.IsNullOrWhiteSpace(req.ExecutedBy))
        {
            var t = req.ExecutedBy.Trim();
            q = q.Where(r => r.LastUpdatedBy != null && EF.Functions.Like(r.LastUpdatedBy, $"%{t}%"));
        }
        if (DateTime.TryParse(req.CreatedDate, out var day))
        {
            var from = day.Date;
            var to   = from.AddDays(1);
            q = q.Where(r => r.CreatedDate >= from && r.CreatedDate < to);
        }

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

        // Pull EntityJson too so we can derive the entity name in memory (JSON
        // parsing isn't EF-translatable, so it can't live in the projection).
        var raw = await q
            .Skip(req.Skip)
            .Take(req.Take)
            .Select(r => new
            {
                r.RequestId,
                r.RequestTypeCode,
                r.EntityStatusCode,
                r.RequestStatusCode,
                r.EntityId,
                r.EntityJson,
                r.CreatedBy,
                r.CreatedDate,
                r.LastUpdatedBy,
                r.LastUpdatedDate
            })
            .ToListAsync(ct);

        var rows = raw.Select(r => new RequestListItemDto
        {
            RequestId         = r.RequestId,
            RequestTypeCode   = r.RequestTypeCode,
            EntityStatusCode  = r.EntityStatusCode,
            RequestStatusCode = r.RequestStatusCode,
            EntityId          = r.EntityId,
            EntityName        = ExtractEntityName(r.RequestTypeCode, r.EntityJson),
            CreatedBy         = r.CreatedBy,
            CreatedDate       = r.CreatedDate,
            ExecutedBy        = r.LastUpdatedBy,
            ExecutedDate      = r.LastUpdatedDate
        }).ToList();

        return new PagedResult<RequestListItemDto>
        {
            items    = rows,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    // Best-effort human label for the entity being changed, parsed from the
    // serialized Entity_Json. Mirrors the FE's entityName() resolution. Tolerant
    // of camelCase / PascalCase keys and bad JSON (returns null).
    private static string? ExtractEntityName(string? requestTypeCode, string? entityJson)
    {
        if (string.IsNullOrWhiteSpace(entityJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(entityJson);
            var root = doc.RootElement;
            string[] keys = requestTypeCode switch
            {
                "0001" => new[] { "UserName", "userName", "EmailAddress", "emailAddress" },
                "0003" => new[] { "RoleName", "roleName" },
                "0004" => new[] { "BranchName", "branchName", "BranchCode", "branchCode" },
                "0005" => new[] { "Name", "name", "FlagName", "flagName" },
                "0006" => new[] { "flagName", "FlagName", "Name", "name" },
                // Field Config: the FE parks display-only productName / sectionLabel
                // alongside the replayable PublishSectionCommand body.
                "0007" => new[] { "sectionLabel", "productName", "ProductName" },
                // Reference Lists — type/value carry name+code; bulk parks an optional
                // display-only typeName alongside the replayable {typeId, rows} body.
                "0008" => new[] { "name", "Name", "code", "Code" },
                "0009" => new[] { "name", "Name", "code", "Code" },
                "0010" => new[] { "typeName", "TypeName", "Name", "name" },
                // Configuration change — the payload parks a display-only displayName
                // (falls back to the raw config key) alongside the replay body.
                "0011" => new[] { "displayName", "DisplayName", "key", "Key" },
                _      => new[] { "Name", "name" },
            };
            foreach (var k in keys)
            {
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    root.TryGetProperty(k, out var v) &&
                    v.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch { /* malformed EntityJson — fall through to null */ }
        return null;
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
