using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.SetActive;

public class SetFlagActiveCommandHandler : IRequestHandler<SetFlagActiveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public SetFlagActiveCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(SetFlagActiveCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Flag activation toggle requires an authenticated caller.");

        var entity = await _db.FlagCatalogues
            .FirstOrDefaultAsync(c => c.FlagId == req.FlagId, ct)
            ?? throw new NotFoundException("flag_not_found",
                $"Flag {req.FlagId} does not exist.");

        // No-op short-circuit — saves a write when the caller is just
        // re-affirming current state (idempotency wins).
        if (entity.ActiveFlag == req.IsActive) return Unit.Value;

        entity.ActiveFlag      = req.IsActive;
        entity.LastUpdatedBy   = userId;
        entity.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
