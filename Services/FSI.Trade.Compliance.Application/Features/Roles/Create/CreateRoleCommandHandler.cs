using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.Create;

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public CreateRoleCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<int> Handle(CreateRoleCommand req, CancellationToken ct)
    {
        var name = req.roleName.Trim();

        // Uniqueness — case-insensitive on Role_Name (matches SQL Server's
        // default collation behaviour; index on the column makes this cheap).
        var taken = await _db.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, ct);
        if (taken)
            throw new ConflictException("role_name_taken", $"A role named '{name}' already exists.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;

        var role = new Role
        {
            TenantId           = 1,                       // single-tenant for now
            Name               = name,
            Description        = string.IsNullOrWhiteSpace(req.roleDescription) ? null : req.roleDescription.Trim(),
            IsActive           = req.isActive,
            EffectiveStartDate = now,
            EffectiveEndDate   = new DateTime(9999, 12, 31),
            CreatedBy          = actor,
            CreatedDate        = now,
            LastUpdatedBy      = null,
            LastUpdatedDate    = null
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);

        return role.Id;
    }
}
