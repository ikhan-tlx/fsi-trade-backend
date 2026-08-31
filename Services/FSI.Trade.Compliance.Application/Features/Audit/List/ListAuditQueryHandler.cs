using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Audit.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Audit.List;

public class ListAuditQueryHandler : IRequestHandler<ListAuditQuery, PagedResult<AuditLogListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAuditQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogListItemDto>> Handle(ListAuditQuery req, CancellationToken ct)
    {
        // Same filter + sort as the export, via the shared builder.
        var q = AuditQueryBuilder.ApplyFilters(_db.AuditLogs.AsNoTracking(), req);
        q = AuditQueryBuilder.ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);
        var items = await q.Skip(req.Skip).Take(req.Take)
            .Select(a => new AuditLogListItemDto
            {
                auditId       = a.AuditId,
                createdDate   = a.CreatedDate,
                userName      = a.UserName,
                userId        = a.UserId,
                userRole      = a.UserRole,
                onBehalfOf    = a.OnBehalfOf,
                ipAddress     = a.IpAddress,
                action        = a.Action,
                entityType    = a.EntityType,
                entityKey     = a.EntityKey,
                description   = a.Description,
                screen        = a.Screen,
                requestPath   = a.RequestPath,
                httpMethod    = a.HttpMethod,
                statusCode    = a.StatusCode,
                outcome       = a.Outcome,
                failureReason = a.FailureReason,
                host          = a.Host,
                application   = a.Application,
                sessionId     = a.SessionId,
                correlationId = a.CorrelationId,
                userAgent     = a.UserAgent,
                rowHash       = a.RowHash,
                prevHash      = a.PrevHash,
                changesJson   = a.ChangesJson,
            })
            .ToListAsync(ct);

        return new PagedResult<AuditLogListItemDto> { items = items, total = total, page = req.Page, pageSize = req.PageSize };
    }
}
