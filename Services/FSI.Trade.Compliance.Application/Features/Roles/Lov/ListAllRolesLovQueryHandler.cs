using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.Lov;

public class ListAllRolesLovQueryHandler
    : IRequestHandler<ListAllRolesLovQuery, IReadOnlyList<RoleLovItemDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAllRolesLovQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleLovItemDto>> Handle(
        ListAllRolesLovQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await _db.Roles
            .AsNoTracking()
            .Where(r => r.IsActive
                     && r.EffectiveStartDate <= now
                     && r.EffectiveEndDate   >= now)
            .OrderBy(r => r.Name)
            .Select(r => new RoleLovItemDto
            {
                roleId             = r.Id,
                roleName           = r.Name,
                roleDescription    = r.Description,
                effectiveStartDate = r.EffectiveStartDate,
                effectiveEndDate   = r.EffectiveEndDate
            })
            .ToListAsync(ct);
    }
}
