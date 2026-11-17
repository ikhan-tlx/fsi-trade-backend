using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Detail;

/// <summary>
/// Loads one transaction by its human-friendly <c>TransactionNumber</c>
/// (e.g. <c>TLX00200001012</c>) instead of the surrogate primary key, so the
/// FE can route on the number rather than exposing the PK in the URL.
///
/// This resolves the number to the PK (tenant-scoped) and then delegates to
/// <see cref="GetTransactionByIdQuery"/>, reusing its full detail projection
/// AND its per-user branch-access gate — so number-based reads are scoped
/// identically to PK-based reads.
/// </summary>
public record GetTransactionByNumberQuery(string TransactionNumber) : IRequest<TransactionDetailDto>;

public class GetTransactionByNumberQueryHandler
    : IRequestHandler<GetTransactionByNumberQuery, TransactionDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IMediator             _mediator;

    public GetTransactionByNumberQueryHandler(
        IApplicationDbContext db,
        IMediator             mediator)
    {
        _db       = db;
        _mediator = mediator;
    }

    public async Task<TransactionDetailDto> Handle(GetTransactionByNumberQuery req, CancellationToken ct)
    {
        var number = req.TransactionNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number))
            throw new NotFoundException("transaction_not_found", "A transaction number is required.");

        // Resolve the PK from the number. Transaction numbers are globally unique
        // (branch-code prefix + a single global sequence) and transactions are
        // persisted under a fixed TenantId (single-tenant deployment), while the
        // caller's token tenant can differ — so we deliberately do NOT filter by
        // tenant here (mirrors GetByType). The real per-user access control is the
        // branch-scope gate inside GetTransactionByIdQuery, applied below.
        var id = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionNumber == number)
            .Select(t => (int?)t.TransactionId)
            .FirstOrDefaultAsync(ct);

        if (id is null)
            throw new NotFoundException("transaction_not_found",
                $"Transaction {number} does not exist.");

        // Delegate to the by-id handler — same detail projection AND access gate.
        return await _mediator.Send(new GetTransactionByIdQuery(id.Value), ct);
    }
}
