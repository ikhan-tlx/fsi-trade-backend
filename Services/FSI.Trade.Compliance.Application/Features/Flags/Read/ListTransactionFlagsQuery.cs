using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Read;

/// <summary>
/// Standalone endpoint for "list the flags applicable to this
/// transaction with their current state". Same projection as the
/// embedded list inside <see cref="GetTransactionByIdQuery"/> — but
/// served on its own URL so admin / stats / integration consumers can
/// fetch just the flag panel without the full transaction payload.
///
/// Maps to <c>GET /api/v1/Transaction/{id}/Flags</c>.
/// </summary>
public record ListTransactionFlagsQuery(int TransactionId)
    : IRequest<IReadOnlyList<TransactionFlagDto>>;
