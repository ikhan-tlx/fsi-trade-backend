using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.List;

public class ListRolesQuery : PagedQuery, IRequest<PagedResult<RoleListItemDto>>
{
}
