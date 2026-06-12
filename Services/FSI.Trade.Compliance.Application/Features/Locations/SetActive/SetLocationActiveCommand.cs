using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Locations.SetActive;

/// <summary>Activate / deactivate a location. Slice 14.</summary>
public record SetLocationActiveCommand(int LocationId, bool Active) : IRequest<Unit>;

public class SetLocationActiveCommandHandler : IRequestHandler<SetLocationActiveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public SetLocationActiveCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<Unit> Handle(SetLocationActiveCommand req, CancellationToken ct)
    {
        var location = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == req.LocationId, ct)
            ?? throw new NotFoundException("location_not_found", $"Location {req.LocationId} not found.");

        if (location.ActiveFlag == req.Active) return Unit.Value;

        location.ActiveFlag      = req.Active;
        location.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        location.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
