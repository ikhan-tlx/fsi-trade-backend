using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.List;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.GetById;

/// <summary>Single branch by id, for the edit form. Slice 14.</summary>
public record GetCompanyBranchByIdQuery(int Id) : IRequest<CompanyBranchListItemDto>;

public class GetCompanyBranchByIdQueryHandler
    : IRequestHandler<GetCompanyBranchByIdQuery, CompanyBranchListItemDto>
{
    private readonly IApplicationDbContext _db;
    public GetCompanyBranchByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CompanyBranchListItemDto> Handle(GetCompanyBranchByIdQuery req, CancellationToken ct)
    {
        return await _db.CompanyBranches.AsNoTracking()
            .Where(b => b.CompanyBranchId == req.Id)
            .Select(b => new CompanyBranchListItemDto
            {
                companyBranchId    = b.CompanyBranchId,
                branchCode         = b.BranchCode,
                branchName         = b.BranchName,
                branchDescription  = b.BranchDescription,
                locationId         = b.LocationId,
                companyId          = b.CompanyId,
                status             = b.Status ?? (b.ActiveFlag ? "Active" : "Inactive"),
                activeFlag         = b.ActiveFlag,
                effectiveStartDate = b.EffectiveStartDate,
                effectiveEndDate   = b.EffectiveEndDate,
                createdDate        = b.CreatedDate
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("branch_not_found", $"Branch {req.Id} not found.");
    }
}
