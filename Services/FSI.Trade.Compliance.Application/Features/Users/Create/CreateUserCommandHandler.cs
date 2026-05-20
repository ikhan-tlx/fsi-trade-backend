using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.Create;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, string>
{
    private readonly IApplicationDbContext      _db;
    private readonly IUserAuthenticationService _userAuth;
    private readonly ICurrentUserService        _current;

    public CreateUserCommandHandler(
        IApplicationDbContext      db,
        IUserAuthenticationService userAuth,
        ICurrentUserService        current)
    {
        _db       = db;
        _userAuth = userAuth;
        _current  = current;
    }

    public async Task<string> Handle(CreateUserCommand req, CancellationToken ct)
    {
        var name = req.userName.Trim();

        var taken = await _db.Users.AsNoTracking().AnyAsync(u => u.UserName == name, ct);
        if (taken)
            throw new ConflictException("user_name_taken", $"A user named '{name}' already exists.");

        if (!string.IsNullOrWhiteSpace(req.emailAddress))
        {
            var email = req.emailAddress.Trim();
            var emailTaken = await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct);
            if (emailTaken)
                throw new ConflictException("user_email_taken", $"A user with email '{email}' already exists.");
        }

        // Validate role IDs upfront — fail loud rather than silently dropping.
        var requestedRoleIds = req.roleIds.Where(id => id > 0).Distinct().ToList();
        if (requestedRoleIds.Count > 0)
        {
            var validRoleIds = await _db.Roles.AsNoTracking()
                .Where(r => requestedRoleIds.Contains(r.Id))
                .Select(r => r.Id)
                .ToListAsync(ct);
            var missing = requestedRoleIds.Except(validRoleIds).ToList();
            if (missing.Count > 0)
                throw new NotFoundException("role_not_found", $"Unknown role IDs: {string.Join(", ", missing)}.");
        }

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;
        var far   = new DateTime(9999, 12, 31);

        var user = new ApplicationUser
        {
            Id                  = Guid.NewGuid().ToString(),
            UserName            = name,
            Email               = string.IsNullOrWhiteSpace(req.emailAddress) ? null : req.emailAddress.Trim(),
            FirstName           = req.firstName?.Trim(),
            MiddleName          = req.middleName?.Trim(),
            LastName            = req.lastName?.Trim(),
            PhoneNumber         = req.phoneNumber?.Trim(),
            LocationId          = req.locationId,
            TenantId            = 1,
            Status              = "Active",
            ActiveFlag          = true,
            LockoutEnabled      = true,
            FirstPasswordChange = true,                    // force change on first login
            EffectiveStartDate  = now,
            EffectiveEndDate    = far,
            CreatedBy           = actor,
            CreatedDate         = now
        };

        // Hashes the password and inserts the row via UserManager + TmxUserStore.
        var (ok, errors) = await _userAuth.CreateUserAsync(user, req.password, ct);
        if (!ok)
            throw new ConflictException("user_create_failed", string.Join("; ", errors));

        // Assign roles in a second SaveChanges. The user row is committed by
        // CreateUserAsync above; failures here would leave a user without
        // roles — the FE can re-call PUT /User/{id} to repair.
        if (requestedRoleIds.Count > 0)
        {
            foreach (var rid in requestedRoleIds)
            {
                _db.UserRoleMappings.Add(new UserRoleMapping
                {
                    TenantId           = user.TenantId,
                    UserId             = user.Id,
                    RoleId             = rid,
                    IsActive           = true,
                    EffectiveStartDate = now,
                    EffectiveEndDate   = null,
                    CreatedBy          = actor,
                    CreatedDate        = now,
                    LastUpdatedBy      = null,
                    LastUpdatedDate    = null
                });
            }
            await _db.SaveChangesAsync(ct);
        }

        return user.Id;
    }
}
