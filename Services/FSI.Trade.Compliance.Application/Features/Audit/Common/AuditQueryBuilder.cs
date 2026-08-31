using Microsoft.EntityFrameworkCore;
using AuditLogEntity = FSI.Trade.Compliance.Domain.Entities.AuditLog;

namespace FSI.Trade.Compliance.Application.Features.Audit.Common;

/// <summary>Filter criteria shared by the audit LIST and EXPORT requests.</summary>
public interface IAuditFilterCriteria
{
    string?   Filter     { get; }
    DateTime? DateFrom   { get; }
    DateTime? DateTo     { get; }
    string?   UserName   { get; }
    string?   EntityType { get; }
    string?   Action     { get; }
    string?   Outcome    { get; }
    string?   Screen     { get; }
}

/// <summary>
/// Shared filter + sort for audit-trail reads, so the paged LIST and the EXPORT
/// apply IDENTICAL criteria (the exported report must match exactly what the
/// operator sees on screen). Free-text <c>Filter</c> plus the structured
/// advanced-filter params, all optional and AND-combined.
/// </summary>
public static class AuditQueryBuilder
{
    public static IQueryable<AuditLogEntity> ApplyFilters(IQueryable<AuditLogEntity> q, IAuditFilterCriteria req)
    {
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

        if (req.DateFrom.HasValue)
            q = q.Where(a => a.CreatedDate >= req.DateFrom.Value);
        if (req.DateTo.HasValue)
        {
            var end = req.DateTo.Value.Date.AddDays(1); // inclusive end-of-day
            q = q.Where(a => a.CreatedDate < end);
        }
        if (!string.IsNullOrWhiteSpace(req.UserName))
        {
            var v = req.UserName.Trim();
            q = q.Where(a => a.UserName != null && EF.Functions.Like(a.UserName, $"%{v}%"));
        }
        if (!string.IsNullOrWhiteSpace(req.EntityType))
        {
            var v = req.EntityType.Trim();
            q = q.Where(a => EF.Functions.Like(a.EntityType, $"%{v}%"));
        }
        if (!string.IsNullOrWhiteSpace(req.Action))
        {
            var v = req.Action.Trim();
            q = q.Where(a => EF.Functions.Like(a.Action, $"%{v}%"));
        }
        if (!string.IsNullOrWhiteSpace(req.Outcome))
        {
            var v = req.Outcome.Trim();
            q = q.Where(a => a.Outcome != null && a.Outcome == v);
        }
        if (!string.IsNullOrWhiteSpace(req.Screen))
        {
            var v = req.Screen.Trim();
            q = q.Where(a => a.Screen != null && EF.Functions.Like(a.Screen, $"%{v}%"));
        }

        return q;
    }

    public static IQueryable<AuditLogEntity> ApplySort(IQueryable<AuditLogEntity> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return q.OrderByDescending(a => a.CreatedDate);
        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return q.OrderByDescending(a => a.CreatedDate);
        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);
        return field switch
        {
            "username"    => desc ? q.OrderByDescending(a => a.UserName)   : q.OrderBy(a => a.UserName),
            "action"      => desc ? q.OrderByDescending(a => a.Action)     : q.OrderBy(a => a.Action),
            "entitytype"  => desc ? q.OrderByDescending(a => a.EntityType) : q.OrderBy(a => a.EntityType),
            "ipaddress"   => desc ? q.OrderByDescending(a => a.IpAddress)  : q.OrderBy(a => a.IpAddress),
            "outcome"     => desc ? q.OrderByDescending(a => a.Outcome)    : q.OrderBy(a => a.Outcome),
            "screen"      => desc ? q.OrderByDescending(a => a.Screen)     : q.OrderBy(a => a.Screen),
            "createddate" => desc ? q.OrderByDescending(a => a.CreatedDate): q.OrderBy(a => a.CreatedDate),
            _             => desc ? q.OrderByDescending(a => a.CreatedDate) : q.OrderBy(a => a.CreatedDate),
        };
    }
}
