using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Audit.List;

public class ListAuditQueryHandler : IRequestHandler<ListAuditQuery, PagedResult<AuditLogListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAuditQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogListItemDto>> Handle(ListAuditQuery req, CancellationToken ct)
    {
        var q = _db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(a =>
                   (a.UserName != null && EF.Functions.Like(a.UserName, $"%{f}%"))
                || EF.Functions.Like(a.EntityType, $"%{f}%")
                || EF.Functions.Like(a.Action, $"%{f}%")
                || (a.Screen != null && EF.Functions.Like(a.Screen, $"%{f}%"))
                || (a.Outcome != null && EF.Functions.Like(a.Outcome, $"%{f}%"))
                || (a.UserRole != null && EF.Functions.Like(a.UserRole, $"%{f}%"))
                || (a.CorrelationId != null && EF.Functions.Like(a.CorrelationId, $"%{f}%"))
                || (a.IpAddress != null && EF.Functions.Like(a.IpAddress, $"%{f}%")));
        }

        q = ApplySort(q, req.Sort);

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

    private static IQueryable<Domain.Entities.AuditLog> ApplySort(IQueryable<Domain.Entities.AuditLog> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return q.OrderByDescending(a => a.CreatedDate);
        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return q.OrderByDescending(a => a.CreatedDate);
        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);
        return field switch
        {
            "username"   => desc ? q.OrderByDescending(a => a.UserName)   : q.OrderBy(a => a.UserName),
            "action"     => desc ? q.OrderByDescending(a => a.Action)     : q.OrderBy(a => a.Action),
            "entitytype" => desc ? q.OrderByDescending(a => a.EntityType) : q.OrderBy(a => a.EntityType),
            _            => desc ? q.OrderByDescending(a => a.CreatedDate) : q.OrderBy(a => a.CreatedDate),
        };
    }
}
