using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Read;

public class ListFlagsByScopeQueryHandler
    : IRequestHandler<ListFlagsByScopeQuery, IReadOnlyList<TransactionFlagDto>>
{
    private readonly IApplicationDbContext _db;

    public ListFlagsByScopeQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransactionFlagDto>> Handle(
        ListFlagsByScopeQuery req, CancellationToken ct)
    {
        return await TransactionFlagProjection.LoadByProductAsync(
            _db, req.ProductId, req.TabId, ct);
    }
}
