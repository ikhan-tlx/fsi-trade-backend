using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Configurations.GetForUser;

public class GetUserCompanyConfigurationsQueryHandler
    : IRequestHandler<GetUserCompanyConfigurationsQuery, List<ConfigurationItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public GetUserCompanyConfigurationsQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<List<ConfigurationItemDto>> Handle(GetUserCompanyConfigurationsQuery req, CancellationToken ct)
    {
        var tenantId = _current.TenantId ?? 1;
        var now      = DateTime.UtcNow;

        return await _db.AppConfigurations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     // Effective-window filter (NULL = no bound). Active_Flag is
                     // not a column on TmX_Configuration in the live schema, so
                     // effective dates are the only lifecycle gate.
                     && (c.EffectiveStartDate == null || c.EffectiveStartDate <= now)
                     && (c.EffectiveEndDate   == null || c.EffectiveEndDate   >= now))
            .OrderBy(c => c.ConfigurationKey)
            .Select(c => new ConfigurationItemDto
            {
                configurationId          = c.Id,
                configurationKey         = c.ConfigurationKey,
                configurationValue       = c.ConfigurationValue,
                configurationDescription = c.ConfigurationDescription,
                productId                = c.ProductId,
                timeZoneId               = c.TimeZoneId
            })
            .ToListAsync(ct);
    }
}
