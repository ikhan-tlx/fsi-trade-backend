using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Cancel;

/// <summary>
/// Cancels an existing transaction. Mirrors legacy
/// <c>PUT /Transaction/CancelById/{transactionId}</c>:
///
/// <list type="number">
///   <item>If the transaction has a workflow instance attached, forces it
///         to the <c>"Application Cancelled"</c> terminal state via
///         <c>IWorkflowEngine.SetStateAsync</c>. The scheme typically has
///         an auto-transition from any state to the terminal that fires
///         cleanup actions (inbox clearing, notifications, etc.).</item>
///   <item>Looks up the <c>APPLICATION_STATUS</c> lookup row for
///         <c>"Application Cancelled"</c> and writes its <c>Lookup_ID</c>
///         to <c>TmX_Transaction.Transaction_Status_Lkp</c>.</item>
/// </list>
///
/// Idempotent: cancelling an already-cancelled transaction is a no-op
/// (workflow setState is silent on a terminal state).
/// </summary>
public class CancelTransactionCommand : IRequest<TransactionDetailDto>
{
    /// <summary>Route-bound transaction id; set by the controller.</summary>
    public int      transactionId { get; set; }

    /// <summary>Optional free-text reason. Logged via SetState's audit channel.</summary>
    public string?  reason        { get; set; }
}
