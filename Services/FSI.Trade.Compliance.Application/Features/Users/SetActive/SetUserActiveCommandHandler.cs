using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.SetActive;

public class SetUserActiveCommandHandler : IRequestHandler<SetUserActiveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public SetUserActiveCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(SetUserActiveCommand req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.UserId}' not found.");

        if (user.ActiveFlag == req.Active)
            return Unit.Value;   // idempotent — no-op if already in requested state

        user.ActiveFlag      = req.Active;
        user.Status          = req.Active ? "Active" : "Inactive";
        user.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        user.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
