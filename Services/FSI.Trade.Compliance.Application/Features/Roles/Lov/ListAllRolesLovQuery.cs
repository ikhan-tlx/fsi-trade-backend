using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Lov;

/// <summary>
/// Slice 7 (Reports) — list-of-values for role dropdowns. Returns ALL
/// effective roles (Active_Flag + today inside effective window). Used
/// by Reports (role-scoped reports), audit screens, and anything else
/// that needs to pick "any role" rather than a privileged subset.
///
/// Distinct from the existing <c>GET /api/v1/Role</c> CRUD list which is
/// privilege-gated (Roles.View) and supports paging/filter/sort — this
/// LOV is intentionally flat, no privilege gate beyond Authenticated.
///
/// Maps to <c>GET /api/v1/Roles</c> (plural — matches FE expectation).
/// The FE businessReports api additionally re-filters by effective dates
/// client-side; we still apply the same filter server-side so an admin
/// can't accidentally surface a retired role.
/// </summary>
public record ListAllRolesLovQuery : IRequest<IReadOnlyList<RoleLovItemDto>>;

public class RoleLovItemDto
{
    public int       roleId             { get; set; }
    public string    roleName           { get; set; } = "";
    public string?   roleDescription    { get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
}
