using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Read;

public class ListTransactionFlagsQueryHandler
    : IRequestHandler<ListTransactionFlagsQuery, IReadOnlyList<TransactionFlagDto>>
{
    private readonly IApplicationDbContext _db;

    public ListTransactionFlagsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransactionFlagDto>> Handle(
        ListTransactionFlagsQuery req, CancellationToken ct)
    {
        // Resolve product first so the projection can scope correctly.
        // 404 here matches the GET /Transaction/{id} behaviour.
        var productId = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionId == req.TransactionId)
            .Select(t => (int?)t.ProductId)
            .FirstOrDefaultAsync(ct);

        if (productId is null)
            throw new NotFoundException("transaction_not_found",
                $"Transaction {req.TransactionId} does not exist.");

        // Only the flags SET on the transaction (TmX_Transaction_Flag rows).
        // The selectable catalogue scoped by product/sub-product is served
        // separately by GET /Flag/Product/{productId}/Tab/{tabId}.
        return await TransactionFlagProjection.LoadSetForTransactionAsync(
            _db, req.TransactionId, productId.Value, ct);
    }
}
