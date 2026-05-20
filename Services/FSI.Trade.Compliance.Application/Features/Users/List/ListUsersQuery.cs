using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.List;

public class ListUsersQuery : PagedQuery, IRequest<PagedResult<UserListItemDto>>
{
}
