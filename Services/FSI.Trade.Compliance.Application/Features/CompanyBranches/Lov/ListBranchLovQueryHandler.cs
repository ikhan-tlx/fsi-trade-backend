using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Lov;

public class ListBranchLovQueryHandler : IRequestHandler<ListBranchLovQuery, IReadOnlyList<BranchLovItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public ListBranchLovQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<IReadOnlyList<BranchLovItemDto>> Handle(ListBranchLovQuery req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Branch LOV requires an authenticated caller.");

        var now = DateTime.UtcNow;

        // INNER JOIN user-branch mappings → branches. User sees only branches
        // they're actively mapped to. Both mapping and branch effective-date
        // windows applied so retired assignments / decommissioned branches
        // don't show up.
        return await (
            from m in _db.CompanyBranchUserMappings.AsNoTracking()
            join b in _db.CompanyBranches.AsNoTracking() on m.CompanyBranchId equals b.CompanyBranchId
            where m.UserId             == userId
               && m.ActiveFlag
               && m.EffectiveStartDate <= now
               && m.EffectiveEndDate   >= now
               && b.ActiveFlag
               && b.EffectiveStartDate <= now
               && b.EffectiveEndDate   >= now
            orderby b.BranchName
            select new BranchLovItemDto
            {
                companyBranchId = b.CompanyBranchId,
                branchCode      = b.BranchCode,
                branchName      = b.BranchName,
                locationId      = b.LocationId
            })
            .Distinct()
            .ToListAsync(ct);
    }
}
