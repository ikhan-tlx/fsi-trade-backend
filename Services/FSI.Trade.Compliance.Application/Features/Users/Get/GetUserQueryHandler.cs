using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Users.Common;
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

        // Current branch assignments (legacy "Company & Branch Association"),
        // joined to the branch row for code/name/company so the edit form
        // prefills without a second round-trip.
        // No Active_Flag gate: the legacy data has current associations stored
        // with Active_Flag = 0 (e.g. this tenant's "Lahore Branch" mapping), and
        // the Users grid already surfaces them. Gating on ActiveFlag here left
        // the edit form's Company/Branch section blank. Load all of the user's
        // mappings so the section prefills consistently with the grid.
        var branchMappings = await _db.CompanyBranchUserMappings
            .AsNoTracking()
            .Where(m => m.UserId == u.Id)
            .Join(
                _db.CompanyBranches.AsNoTracking(),
                m => m.CompanyBranchId,
                b => b.CompanyBranchId,
                (m, b) => new UserBranchRefDto
                {
                    companyBranchId    = b.CompanyBranchId,
                    branchCode         = b.BranchCode,
                    branchName         = b.BranchName,
                    companyId          = b.CompanyId,
                    reportingBossId    = m.ReportingBossId,
                    effectiveStartDate = m.EffectiveStartDate,
                    effectiveEndDate   = m.EffectiveEndDate,
                    isActive           = m.ActiveFlag,
                    visibleValue       = string.IsNullOrWhiteSpace(b.BranchCode)
                                        ? b.BranchName
                                        : b.BranchCode + "-" + b.BranchName
                })
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
            accountStatus       = UserAccountStatus.Derive(u.ActiveFlag, u.LockoutEndDateUtc, u.PasswordExpiryDate, now),
            isActive            = u.ActiveFlag,
            isLockedOut         = u.LockoutEndDateUtc.HasValue && u.LockoutEndDateUtc.Value > now,
            lockoutEndDateUtc   = u.LockoutEndDateUtc,
            lockoutReason       = u.LockoutReason,
            accessFailedCount   = u.AccessFailedCount,
            lockedAt            = u.LockedAt,
            lockedBy            = u.LockedBy,
            unlockedAt          = u.UnlockedAt,
            unlockedBy          = u.UnlockedBy,
            deactivatedAt       = u.DeactivatedAt,
            deactivatedBy       = u.DeactivatedBy,
            twoFactorEnabled    = u.TwoFactorEnabled,
            firstPasswordChange = u.FirstPasswordChange,
            mustChangePassword  = u.MustChangePassword,
            passwordExpiryDate  = u.PasswordExpiryDate,
            lastLoginDate       = u.LastLoginDate,
            lastFailedLoginDate = u.LastFailedLoginDate,
            effectiveStartDate  = u.EffectiveStartDate,
            effectiveEndDate    = u.EffectiveEndDate,
            createdDate         = u.CreatedDate,
            createdBy           = u.CreatedBy,
            lastUpdatedDate     = u.LastUpdatedDate,
            lastUpdatedBy       = u.LastUpdatedBy,
            roles               = roles,
            companyBranchMappings = branchMappings
        };
    }
}
