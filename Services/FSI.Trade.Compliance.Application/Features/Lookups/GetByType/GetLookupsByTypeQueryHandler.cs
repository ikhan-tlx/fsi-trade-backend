using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByType;

public class GetLookupsByTypeQueryHandler
    : IRequestHandler<GetLookupsByTypeQuery, IReadOnlyList<LookupItemDto>>
{
    private readonly IApplicationDbContext _db;

    public GetLookupsByTypeQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LookupItemDto>> Handle(GetLookupsByTypeQuery req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Type))
            return Array.Empty<LookupItemDto>();

        var lookupType   = req.Type.Trim();
        var cultureLower = (req.Culture ?? "en").Trim().ToLowerInvariant();

        // Tenant-agnostic by design: the lookup catalogue is shared reference data
        // (values live under the platform tenant) and the grouped v2 catalogue is
        // already tenant-agnostic, so filtering single-type reads by the caller's
        // tenant only hid rows (e.g. values stored under tenant 1 were invisible to
        // a tenant-245 caller). Culture match mirrors GetLookupsByCultureQueryHandler:
        // string-prefix against LocaleLabel with a fallback to culture-agnostic (null) rows.
        return await _db.Lookups
            .AsNoTracking()
            .Where(l => l.LookupType == lookupType
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
