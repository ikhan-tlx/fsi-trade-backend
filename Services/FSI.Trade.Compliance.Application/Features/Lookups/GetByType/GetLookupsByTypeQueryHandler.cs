using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByType;

public class GetLookupsByTypeQueryHandler
    : IRequestHandler<GetLookupsByTypeQuery, IReadOnlyList<LookupItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public GetLookupsByTypeQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<IReadOnlyList<LookupItemDto>> Handle(GetLookupsByTypeQuery req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Type))
            return Array.Empty<LookupItemDto>();

        var tenantId     = _current.TenantId ?? 1;
        var lookupType   = req.Type.Trim();
        var cultureLower = (req.Culture ?? "en").Trim().ToLowerInvariant();

        // Culture match mirrors GetLookupsByCultureQueryHandler: string-prefix
        // against LocaleLabel with a fallback to culture-agnostic (null) rows.
        return await _db.Lookups
            .AsNoTracking()
            .Where(l => l.TenantId   == tenantId
                     && l.LookupType == lookupType
                     && (l.LocaleLabel == null
                      || l.LocaleLabel.ToLower().StartsWith(cultureLower)))
            .OrderBy(l => l.SortOrder)
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
    }
}
