using System.Text.Json.Serialization;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Requests.GetById;

/// <summary>
/// Single maker/checker request (the Requests edit/detail page). Slice 14.
/// Returns the row plus its serialized <c>Entity_Json</c> — the FE parses that
/// into <c>RequestEntity</c> and renders the User / Role / Branch form in view
/// mode. DTO keys are PascalCase ([JsonPropertyName]) because the FE reads
/// RequestTypeCode / EntityJson / etc.
/// </summary>
public record GetRequestByIdQuery(long RequestId) : IRequest<RequestDetailDto>;

public class RequestDetailDto
{
    [JsonPropertyName("RequestId")]         public long      RequestId         { get; set; }
    [JsonPropertyName("RequestTypeCode")]   public string?   RequestTypeCode   { get; set; }
    [JsonPropertyName("EntityStatusCode")]  public string?   EntityStatusCode  { get; set; }
    [JsonPropertyName("RequestStatusCode")] public string?   RequestStatusCode { get; set; }
    [JsonPropertyName("EntityId")]          public string?   EntityId          { get; set; }
    [JsonPropertyName("EntityJson")]        public string?   EntityJson        { get; set; }
    [JsonPropertyName("RejectReason")]      public string?   RejectReason      { get; set; }
    [JsonPropertyName("CreatedBy")]         public string?   CreatedBy         { get; set; }
    [JsonPropertyName("CreatedDate")]       public DateTime  CreatedDate       { get; set; }
    [JsonPropertyName("ExecutedBy")]        public string?   ExecutedBy        { get; set; }
}

public class GetRequestByIdQueryHandler : IRequestHandler<GetRequestByIdQuery, RequestDetailDto>
{
    private readonly IApplicationDbContext _db;
    public GetRequestByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<RequestDetailDto> Handle(GetRequestByIdQuery req, CancellationToken ct)
    {
        var r = await _db.Requests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == req.RequestId, ct)
            ?? throw new NotFoundException("request_not_found", $"Request '{req.RequestId}' not found.");

        return new RequestDetailDto
        {
            RequestId         = r.RequestId,
            RequestTypeCode   = r.RequestTypeCode,
            EntityStatusCode  = r.EntityStatusCode,
            RequestStatusCode = r.RequestStatusCode,
            EntityId          = r.EntityId,
            EntityJson        = r.EntityJson,
            RejectReason      = r.RejectReason,
            CreatedBy         = r.CreatedBy,
            CreatedDate       = r.CreatedDate,
            ExecutedBy        = r.LastUpdatedBy
        };
    }
}
