using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Users.List;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.Get;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDetailDto>
{
    private readonly IApplicationDbContext _db;
    public GetUserQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<UserDetailDto> Handle(GetUserQuery req, CancellationToken ct)
    {
        var u = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == req.UserId, ct);
        if (u is null)
            throw new NotFoundException("user_not_found", $"User '{req.UserId}' not found.");

        var now   = DateTime.UtcNow;
        var roles = await _db.UserRoleMappings
            .AsNoTracking()
            .Where(urm => urm.UserId == u.Id)
            .Join(
                _db.Roles.AsNoTracking(),
                urm => urm.RoleId,
                r   => r.Id,
                (_, r) => new UserRoleRefDto { roleId = r.Id, roleName = r.Name })
            .ToListAsync(ct);

        return new UserDetailDto
        {
            userId              = u.Id,
            userName            = u.UserName,
            emailAddress        = u.Email,
            firstName           = u.FirstName,
            middleName          = u.MiddleName,
            lastName            = u.LastName,
            phoneNumber         = u.PhoneNumber,
            imageUrl            = u.ImageURL,
            tenantId            = u.TenantId,
            locationId          = u.LocationId,
            userTypeLkpId       = u.UserTypeLkpId,
            designationLkpId    = u.DesignationLkpId,
            status              = u.Status,
            isActive            = u.ActiveFlag,
            isLockedOut         = u.LockoutEndDateUtc.HasValue && u.LockoutEndDateUtc.Value > now,
            twoFactorEnabled    = u.TwoFactorEnabled,
            firstPasswordChange = u.FirstPasswordChange,
            passwordExpiryDate  = u.PasswordExpiryDate,
            lastLoginDate       = u.LastLoginDate,
            effectiveStartDate  = u.EffectiveStartDate,
            effectiveEndDate    = u.EffectiveEndDate,
            createdDate         = u.CreatedDate,
            createdBy           = u.CreatedBy,
            lastUpdatedDate     = u.LastUpdatedDate,
            lastUpdatedBy       = u.LastUpdatedBy,
            roles               = roles
        };
    }
}
