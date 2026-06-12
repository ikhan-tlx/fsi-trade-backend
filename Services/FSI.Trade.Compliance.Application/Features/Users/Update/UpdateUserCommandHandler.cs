using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.Update;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateUserCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(UpdateUserCommand req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.userId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.userId}' not found.");

        // Email uniqueness — only check if changing.
        if (!string.IsNullOrWhiteSpace(req.emailAddress))
        {
            var newEmail = req.emailAddress.Trim();
            if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                var emailTaken = await _db.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Id != req.userId && u.Email == newEmail, ct);
                if (emailTaken)
                    throw new ConflictException("user_email_taken", $"A user with email '{newEmail}' already exists.");
            }
        }

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;

        // Update profile fields
        user.Email           = string.IsNullOrWhiteSpace(req.emailAddress) ? user.Email : req.emailAddress.Trim();
        user.FirstName       = req.firstName?.Trim()  ?? user.FirstName;
        user.MiddleName      = req.middleName?.Trim() ?? user.MiddleName;
        user.LastName        = req.lastName?.Trim()   ?? user.LastName;
        user.PhoneNumber     = req.phoneNumber?.Trim()?? user.PhoneNumber;
        user.LocationId      = req.locationId         ?? user.LocationId;
        user.LastUpdatedBy   = actor;
        user.LastUpdatedDate = now;

        // Role re-assignment — only if the FE explicitly sent a roleIds array
        // (null = don't touch). Empty array = revoke all roles.
        if (req.roleIds is not null)
        {
            var requested = req.roleIds.Where(id => id > 0).Distinct().ToList();

            if (requested.Count > 0)
            {
                var validRoleIds = await _db.Roles.AsNoTracking()
                    .Where(r => requested.Contains(r.Id))
                    .Select(r => r.Id)
                    .ToListAsync(ct);
                var missing = requested.Except(validRoleIds).ToList();
                if (missing.Count > 0)
                    throw new NotFoundException("role_not_found", $"Unknown role IDs: {string.Join(", ", missing)}.");
            }

            var existing = await _db.UserRoleMappings
                .Where(urm => urm.UserId == user.Id)
                .ToListAsync(ct);
            var existingIds = existing.Select(e => e.RoleId).ToHashSet();

            var toAdd    = requested.Where(id => !existingIds.Contains(id)).ToList();
            var toRemove = existing.Where(e => !requested.Contains(e.RoleId)).ToList();

            foreach (var rid in toAdd)
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
                    CreatedDate        = now
                });
            }

            if (toRemove.Count > 0)
                _db.UserRoleMappings.RemoveRange(toRemove);
        }

        // Company-branch re-assignment — only if the FE explicitly sent the
        // array (null = don't touch; empty = revoke all). Replace-in-full:
        // drop existing mappings and re-insert the desired set. Simple and
        // correct for the assignment volumes here.
        if (req.companyBranchMappings is not null)
        {
            var desired = req.companyBranchMappings
                .Where(m => m.companyBranchId > 0)
                .GroupBy(m => m.companyBranchId)
                .Select(g => g.First())
                .ToList();

            var existingBranches = await _db.CompanyBranchUserMappings
                .Where(m => m.UserId == user.Id)
                .ToListAsync(ct);

            if (existingBranches.Count > 0)
                _db.CompanyBranchUserMappings.RemoveRange(existingBranches);

            var far = new DateTime(9999, 12, 31);
            foreach (var m in desired)
            {
                _db.CompanyBranchUserMappings.Add(new CompanyBranchUserMapping
                {
                    CompanyBranchId    = m.companyBranchId,
                    TenantId           = user.TenantId,
                    UserId             = user.Id,
                    ReportingBossId    = string.IsNullOrWhiteSpace(m.reportingBossId) ? null : m.reportingBossId,
                    EffectiveStartDate = now,
                    EffectiveEndDate   = far,
                    ActiveFlag         = true,
                    CreatedBy          = actor,
                    CreatedDate        = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
