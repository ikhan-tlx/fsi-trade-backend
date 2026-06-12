using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Locations.Create;

/// <summary>
/// Creates a location. Slice 14 (direct CRUD). Field names mirror the FE
/// LocationFormValues. TmX_Location requires Location_Code and
/// Location_Type_Lkp_ID (NOT NULL) — validated here. (The FE "Description"
/// field has no column in TmX_Location, so it isn't persisted.)
/// </summary>
public class CreateLocationCommand : IRequest<int>
{
    public string    LocationCode       { get; set; } = "";
    public string?   LocationName       { get; set; }
    public int?      ParentLocationId   { get; set; }
    public int?      LocationTypeLkpId  { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
}

public class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public CreateLocationCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<int> Handle(CreateLocationCommand req, CancellationToken ct)
    {
        var code = (req.LocationCode ?? "").Trim();
        if (code.Length == 0)
            throw new ConflictException("location_code_required", "Location code is required.");
        if (req.LocationTypeLkpId is null)
            throw new ConflictException("location_type_required", "Location type is required.");

        var exists = await _db.Locations.AsNoTracking()
            .AnyAsync(l => l.LocationCode == code, ct);
        if (exists)
            throw new ConflictException("location_code_taken", $"Location code '{code}' already exists.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var location = new Location
        {
            TenantId           = _current.TenantId ?? 1,
            LocationCode       = code,
            LocationName       = req.LocationName?.Trim(),
            ParentLocationId   = req.ParentLocationId,
            LocationTypeLkpId  = req.LocationTypeLkpId!.Value,
            ActiveFlag         = true,
            EffectiveStartDate = req.EffectiveStartDate ?? DateTime.UtcNow,
            EffectiveEndDate   = req.EffectiveEndDate ?? new DateTime(9999, 12, 31),
            CreatedBy          = actor,
            CreatedDate        = DateTime.UtcNow,
        };

        _db.Locations.Add(location);
        await _db.SaveChangesAsync(ct);
        return location.Id;
    }
}
