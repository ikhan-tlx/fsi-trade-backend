using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Audit.List;

/// <summary>Paged audit-trail listing — GET /api/v1/Audit?page=&amp;pageSize=&amp;sort=&amp;filter=</summary>
public class ListAuditQuery : PagedQuery, IRequest<PagedResult<AuditLogListItemDto>>
{
}

public class AuditLogListItemDto
{
    public long     auditId       { get; set; }
    public DateTime createdDate   { get; set; }
    public string?  userName      { get; set; }
    public string?  userId        { get; set; }
    public string?  userRole      { get; set; }
    public string?  onBehalfOf    { get; set; }
    public string?  ipAddress     { get; set; }
    public string   action        { get; set; } = "";
    public string   entityType    { get; set; } = "";
    public string?  entityKey     { get; set; }
    public string?  description   { get; set; }
    public string?  screen        { get; set; }
    public string?  requestPath   { get; set; }
    public string?  httpMethod    { get; set; }
    public int?     statusCode    { get; set; }
    public string?  outcome       { get; set; }
    public string?  failureReason { get; set; }
    public string?  host          { get; set; }
    public string?  application   { get; set; }
    public string?  sessionId     { get; set; }
    public string?  correlationId { get; set; }
    public string?  userAgent     { get; set; }
    public string?  rowHash       { get; set; }
    public string?  prevHash      { get; set; }
    public string?  changesJson   { get; set; }
}
