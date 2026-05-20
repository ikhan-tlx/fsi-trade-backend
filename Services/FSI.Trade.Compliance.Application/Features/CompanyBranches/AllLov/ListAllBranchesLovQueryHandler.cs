using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.AllLov;

public class ListAllBranchesLovQueryHandler
    : IRequestHandler<ListAllBranchesLovQuery, IReadOnlyList<AllBranchesLovItemDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAllBranchesLovQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AllBranchesLovItemDto>> Handle(
        ListAllBranchesLovQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await _db.CompanyBranches
            .AsNoTracking()
            .Where(b => b.ActiveFlag
                     && b.EffectiveStartDate <= now
                     && b.EffectiveEndDate   >= now)
            .OrderBy(b => b.BranchName)
            .Select(b => new AllBranchesLovItemDto
            {
                companyBranchId = b.CompanyBranchId,
                branchCode      = b.BranchCode,
                branchName      = b.BranchName,
                locationId      = b.LocationId
            })
            .ToListAsync(ct);
    }
}
