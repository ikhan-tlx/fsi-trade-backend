using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

public class UpdateFlagScopeCommandHandler : IRequestHandler<UpdateFlagScopeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateFlagScopeCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(UpdateFlagScopeCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Scope update requires an authenticated caller.");

        var entity = await _db.FlagScopes
            .FirstOrDefaultAsync(s => s.FlagScopeId == req.flagScopeId, ct)
            ?? throw new NotFoundException("flag_scope_not_found",
                $"Flag scope {req.flagScopeId} does not exist.");

        var changed = false;

        if (req.activeFlag.HasValue && req.activeFlag.Value != entity.ActiveFlag)
        {
            entity.ActiveFlag = req.activeFlag.Value;
            changed = true;
        }

        if (req.sortOrder.HasValue && req.sortOrder.Value != entity.SortOrder)
        {
            entity.SortOrder = req.sortOrder.Value;
            changed = true;
        }

        if (!changed) return Unit.Value;

        entity.LastUpdatedBy   = userId;
        entity.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
