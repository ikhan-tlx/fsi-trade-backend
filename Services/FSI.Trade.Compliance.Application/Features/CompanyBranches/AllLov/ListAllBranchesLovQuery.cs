using MediatR;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.AllLov;

/// <summary>
/// Slice 7 (Reports) — list-of-values for branch dropdowns that need ALL
/// effective branches regardless of which branches the caller is mapped
/// to. Used by Reports (e.g. branch-scoped reports for audit users who
/// span the whole bank) and by other admin flows that legitimately need
/// to see every branch.
///
/// This is intentionally a DIFFERENT query from <see cref="Lov.ListBranchLovQuery"/>
/// which user-scopes via TmX_Company_Branch_Users_Mapping. Two separate
/// endpoints, two separate semantics — same DTO shape so the FE can
/// reuse its dropdown component.
///
/// Maps to <c>GET /api/v1/CompanyBranches</c> (plural — matches the FE
/// businessReports api's expected path).
/// </summary>
public record ListAllBranchesLovQuery : IRequest<IReadOnlyList<AllBranchesLovItemDto>>;

// Property names follow the codebase's camelCase-first-letter convention
// (see Lov/BranchLovItemDto) — System.Text.Json's default web policy
// preserves them as-is on the wire. The legacy FE businessReports api
// expects PascalCase; that mismatch is captured in FE_CHANGES_REQUIRED.md.
public class AllBranchesLovItemDto
{
    public int     companyBranchId { get; set; }
    public string  branchCode      { get; set; } = "";
    public string? branchName      { get; set; }
    public int?    locationId      { get; set; }
}
