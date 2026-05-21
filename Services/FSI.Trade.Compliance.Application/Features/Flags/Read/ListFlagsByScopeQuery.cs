using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Read;

/// <summary>
/// "Give me every flag applicable to this product (optionally narrowed
/// to a specific tab)." Used during the transaction create-flow before
/// a <c>Transaction_Id</c> exists — the FE needs to render the flag
/// panel for the analyst even though no transaction-flag rows yet exist.
///
/// Exposed via two API routes that both reach this query:
///   • <c>GET /api/v1/Flag/Product/{productId}</c>           (TabId = null)
///   • <c>GET /api/v1/Flag/Product/{productId}/Tab/{tabId}</c>
///
/// Returns the same DTO shape as the transaction-scoped flag list, but
/// every entry has <c>isFlagged = false</c> + null transaction-state
/// fields so the FE renders an empty panel ready to be ticked.
/// </summary>
public record ListFlagsByScopeQuery(int ProductId, int? TabId)
    : IRequest<IReadOnlyList<TransactionFlagDto>>;
