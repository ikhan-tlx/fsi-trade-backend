using MediatR;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Lov;

/// <summary>
/// List-of-values for company-branch dropdowns. Slice 6.5 — Replaces legacy
/// <c>GET /api/v1/CompanyBranch/lov</c>.
///
/// Semantics: returns the EFFECTIVE branches the AUTHENTICATED caller is
/// MAPPED to (via <c>TmX_Company_Branch_Users_Mapping</c>). This narrows the
/// dropdown to branches the user can actually create transactions for.
/// Legacy <c>BranchLov</c> returned ALL effective branches; the new backend
/// is more security-conscious and respects the caller's mapping. The
/// downstream <c>POST /Transaction</c> handler also validates this mapping,
/// so the two are consistent.
///
/// If a need arises later for "all branches" (e.g. admin-only flow), add
/// a separate <c>?scope=all</c> query parameter — don't change the
/// default which should remain user-scoped.
/// </summary>
public record ListBranchLovQuery : IRequest<IReadOnlyList<BranchLovItemDto>>;

public class BranchLovItemDto
{
    public int     companyBranchId { get; set; }
    public string  branchCode      { get; set; } = "";
    public string? branchName      { get; set; }
    public int?    locationId      { get; set; }
}
