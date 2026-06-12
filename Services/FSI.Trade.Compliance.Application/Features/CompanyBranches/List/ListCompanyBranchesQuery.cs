using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.List;

/// <summary>
/// Paged admin list of company branches for the FE "Company Branches"
/// management grid (<c>GET /api/v1/CompanyBranch?page=…&amp;pageSize=…&amp;sort=…&amp;filter=…</c>).
///
/// Distinct from the two LOV reads on the same controller:
///   /lov — user-scoped dropdown   ·   /all — every active branch (flat)
/// This one is the full paged grid with server-side sort + filter, mirroring
/// the User / Role list pattern (inherits <see cref="PagedQuery"/>).
/// </summary>
public class ListCompanyBranchesQuery : PagedQuery, IRequest<PagedResult<CompanyBranchListItemDto>>
{
}

public class CompanyBranchListItemDto
{
    public int       companyBranchId    { get; set; }
    public string    branchCode         { get; set; } = "";
    public string?   branchName         { get; set; }
    public string?   branchDescription  { get; set; }
    public int?      locationId         { get; set; }
    public int       companyId          { get; set; }
    public string?   status             { get; set; }
    public bool      activeFlag         { get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
    public DateTime  createdDate        { get; set; }
}
