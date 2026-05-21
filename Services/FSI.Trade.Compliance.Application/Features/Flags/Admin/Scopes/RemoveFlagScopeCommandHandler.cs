using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

public class RemoveFlagScopeCommandHandler : IRequestHandler<RemoveFlagScopeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public RemoveFlagScopeCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(RemoveFlagScopeCommand req, CancellationToken ct)
    {
        // ICurrentUserService isn't used here other than to confirm the
        // caller is authenticated — preserves the audit semantic that
        // every mutation runs in the context of a real user even when
        // the row being removed has no user-attribution column to write.
        _ = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Scope removal requires an authenticated caller.");

        var entity = await _db.FlagScopes
            .FirstOrDefaultAsync(s => s.FlagScopeId == req.FlagScopeId, ct)
            ?? throw new NotFoundException("flag_scope_not_found",
                $"Flag scope {req.FlagScopeId} does not exist.");

        _db.FlagScopes.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
