using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Delete;

public class DeleteFlagCommandHandler : IRequestHandler<DeleteFlagCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public DeleteFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(DeleteFlagCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Flag deletion requires an authenticated caller.");

        var entity = await _db.FlagCatalogues
            .FirstOrDefaultAsync(c => c.FlagId == req.FlagId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("flag_not_found",
                $"Flag {req.FlagId} does not exist.");

        // Built-ins are shipped flags — relabel / deactivate only.
        if (entity.IsBuiltIn)
            throw new ConflictException("flag_is_builtin",
                "Built-in flags cannot be deleted. Deactivate it instead.");

        var now = DateTime.UtcNow;
        entity.IsDeleted       = true;
        entity.DeletedBy       = userId;
        entity.DeletedDate     = now;
        // Retiring it also drops it from active read paths immediately.
        entity.ActiveFlag      = false;
        entity.LastUpdatedBy   = userId;
        entity.LastUpdatedDate = now;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
