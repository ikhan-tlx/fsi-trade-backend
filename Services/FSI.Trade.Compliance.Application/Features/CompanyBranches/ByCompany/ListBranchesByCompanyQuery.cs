using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.ByCompany;

/// <summary>
/// Branches that belong to a given company — the cascade the legacy user form
/// used (pick company → its branches load via API). Slice 14. Returns LOV-shaped
/// rows (companyBranchId / branchName) for the "Company Branch" dropdown.
/// </summary>
public record ListBranchesByCompanyQuery(int CompanyId) : IRequest<IReadOnlyList<BranchByCompanyDto>>;

public class BranchByCompanyDto
{
    public int     companyBranchId { get; set; }
    public string? branchCode      { get; set; }
    public string? branchName      { get; set; }
    public int     companyId       { get; set; }
    public string? visibleValue    { get; set; }   // "Code-Name" for the dropdown label
}

public class ListBranchesByCompanyQueryHandler
    : IRequestHandler<ListBranchesByCompanyQuery, IReadOnlyList<BranchByCompanyDto>>
{
    private readonly IApplicationDbContext _db;
    public ListBranchesByCompanyQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<BranchByCompanyDto>> Handle(ListBranchesByCompanyQuery req, CancellationToken ct)
    {
        var rows = await _db.CompanyBranches
            .AsNoTracking()
            .Where(b => b.CompanyId == req.CompanyId)
            .OrderBy(b => b.BranchName)
            .Select(b => new { b.CompanyBranchId, b.BranchCode, b.BranchName, b.CompanyId })
            .ToListAsync(ct);

        return rows.Select(b => new BranchByCompanyDto
        {
            companyBranchId = b.CompanyBranchId,
            branchCode      = b.BranchCode,
            branchName      = b.BranchName,
            companyId       = b.CompanyId,
            visibleValue    = string.IsNullOrWhiteSpace(b.BranchCode) ? b.BranchName : $"{b.BranchCode}-{b.BranchName}",
        }).ToList();
    }
}
