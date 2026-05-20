using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Privileges.ListEntities;

/// <summary>
/// Returns every privilege defined in the system, grouped by entity prefix.
/// Drives the FE role-edit privilege matrix UI. No paging — the catalog is small.
/// </summary>
public record ListPrivilegeEntitiesQuery : IRequest<List<PrivilegeEntityDto>>;
