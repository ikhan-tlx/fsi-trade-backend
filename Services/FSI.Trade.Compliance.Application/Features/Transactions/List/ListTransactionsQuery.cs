using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.List;

/// <summary>
/// Trade Repository grid feed. Mirrors the legacy
/// <c>TransactionService.GetList(request, userId)</c> behaviour:
///
/// <list type="bullet">
///   <item>If the caller has a <c>Location_ID</c>, scope to that location +
///         all descendants (via <c>ILocationHierarchyService</c>).</item>
///   <item>Otherwise, scope to every <c>Company_Branch_Id</c> in the
///         caller's branch-user mapping (effective today).</item>
///   <item>OR (always) anything they themselves created
///         (<c>Creator_Id == userId</c>).</item>
/// </list>
///
/// Inherits <see cref="PagedQuery"/> for <c>Page / PageSize / Sort / Filter</c>.
/// </summary>
public class ListTransactionsQuery : PagedQuery, IRequest<PagedResult<TransactionListItemDto>>
{
}
