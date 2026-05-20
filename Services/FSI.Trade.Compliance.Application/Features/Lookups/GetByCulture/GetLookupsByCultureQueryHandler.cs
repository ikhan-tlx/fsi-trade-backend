using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;

public class GetLookupsByCultureQueryHandler
    : IRequestHandler<GetLookupsByCultureQuery, List<LookupItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public GetLookupsByCultureQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<List<LookupItemDto>> Handle(GetLookupsByCultureQuery req, CancellationToken ct)
    {
        // NOTE — Active_Flag intentionally NOT filtered (per FSI direction). The
        // FE may filter by isActive client-side for display.
        // Tenant filter applies by default (each tenant has its own lookup space);
        // fall back to tenant=1 if the JWT lacks the claim (single-tenant deploy).
        var tenantId = _current.TenantId ?? 1;

        // Culture matching: legacy uses sp_GetLookupByCulture(culture) which
        // resolves a Locale_ID via a separate locale table we haven't ported
        // yet. Until that lands, we do a string-prefix match against
        // Locale_Label (e.g. "en-US", "ar-SA") with a fallback to rows where
        // Locale_Label is null (those are culture-agnostic rows).
        var cultureLower = (req.Culture ?? "en").Trim().ToLowerInvariant();

        var rows = await _db.Lookups
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId
                     && (l.LocaleLabel == null
                      || l.LocaleLabel.ToLower().StartsWith(cultureLower)))
            .OrderBy(l => l.LookupType)
            .ThenBy(l => l.SortOrder)
            .ThenBy(l => l.VisibleValue)
            .Select(l => new LookupItemDto
            {
                lookupId       = l.Id,
                parentLookupId = l.ParentLookupId,
                lookupType     = l.LookupType ?? "",
                lookupName     = l.LookupName,
                visibleValue   = l.VisibleValue,
                hiddenValue    = l.HiddenValue,
                localeLabel    = l.LocaleLabel,
                sortOrder      = l.SortOrder,
                isActive       = l.IsActive
            })
            .ToListAsync(ct);

        return rows;
    }
}
