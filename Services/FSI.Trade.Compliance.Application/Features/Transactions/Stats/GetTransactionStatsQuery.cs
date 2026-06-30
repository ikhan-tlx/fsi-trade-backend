using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Stats;

/// <summary>
/// KPI feed for the Trade Repository listing header. No paging/filter —
/// returns the caller-scoped counts the FE renders as clickable KPI cards
/// and the per-product segment control. Scope mirrors
/// <c>ListTransactionsQuery</c> exactly so the totals reconcile with the grid.
/// </summary>
public class GetTransactionStatsQuery : IRequest<TransactionStatsDto>
{
}
