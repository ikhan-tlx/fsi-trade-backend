namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maker/checker request row over <c>TmX_Request</c>. Slice 14 — added so the
/// FE's "any pending request for this entity?" check
/// (GET /api/v1/Request/GetPendingRequests/{type}/{entityId}) stops 404'ing.
/// </summary>
public class Request
{
    public long      RequestId         { get; set; }
    public int       TenantId          { get; set; }
    public string?   RequestTypeCode   { get; set; }
    public string?   EntityStatusCode  { get; set; }
    public string?   RequestStatusCode { get; set; }
    public string?   EntityId          { get; set; }
    public string?   EntityJson        { get; set; }   // serialized entity being maker/checker'd
    public string?   CommandKey        { get; set; }   // replay marker: "{type}:{action}" for rows created by the generic Submit endpoint (null = legacy row, not auto-replayed)
    public string?   RejectReason      { get; set; }   // checker reject reason / comment
    public string?   CreatedBy         { get; set; }
    public DateTime  CreatedDate       { get; set; }
    public string?   LastUpdatedBy     { get; set; }   // the checker who executed (approve/reject)
    public DateTime? LastUpdatedDate   { get; set; }
}
