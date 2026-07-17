using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Audit.List;

/// <summary>Paged audit-trail listing — GET /api/v1/Audit?page=&amp;pageSize=&amp;sort=&amp;filter=
/// Plus structured advanced-filter params (all optional, AND-combined):
/// dateFrom, dateTo (on createdDate), userName, entityType, action, outcome, screen.</summary>
public class ListAuditQuery : PagedQuery, IRequest<PagedResult<AuditLogListItemDto>>
{
    /// <summary>Inclusive lower bound on createdDate (UTC).</summary>
    public DateTime? DateFrom { get; set; }
    /// <summary>Upper bound on createdDate — the whole calendar day is included.</summary>
    public DateTime? DateTo { get; set; }
    /// <summary>LIKE match on the acting user's name.</summary>
    public string? UserName { get; set; }
    /// <summary>LIKE match on the audited entity type.</summary>
    public string? EntityType { get; set; }
    /// <summary>LIKE match on the action.</summary>
    public string? Action { get; set; }
    /// <summary>Exact match on outcome (e.g. Success / Failure).</summary>
    public string? Outcome { get; set; }
    /// <summary>LIKE match on the originating screen.</summary>
    public string? Screen { get; set; }
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
