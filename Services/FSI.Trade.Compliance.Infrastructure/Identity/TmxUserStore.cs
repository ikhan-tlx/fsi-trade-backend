using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

/// <summary>
/// Custom IUserStore wired over [TmX_User]. Implements only what we use:
/// password, security stamp, lockout, email, phone, two-factor, authenticator key,
/// queryable. No claim/login/role/token stores → no AspNet* tables created or read.
/// </summary>
public class TmxUserStore :
    IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserPhoneNumberStore<ApplicationUser>,
    IUserTwoFactorStore<ApplicationUser>,
    IUserAuthenticatorKeyStore<ApplicationUser>,
    IQueryableUserStore<ApplicationUser>
{
    private readonly IApplicationDbContext _db;
    public TmxUserStore(IApplicationDbContext db) => _db = db;

    public IQueryable<ApplicationUser> Users => _db.Users;

    public void Dispose() { /* DbContext lifetime managed by DI */ }

    // ---------- IUserStore ----------
    public Task<string>  GetUserIdAsync(ApplicationUser u, CancellationToken ct)              => Task.FromResult(u.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser u, CancellationToken ct)            => Task.FromResult(u.UserName);
    public Task          SetUserNameAsync(ApplicationUser u, string? n, CancellationToken ct) { u.UserName = n; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser u, CancellationToken ct)  => Task.FromResult(u.UserName?.ToUpperInvariant());
    public Task          SetNormalizedUserNameAsync(ApplicationUser u, string? n, CancellationToken ct) => Task.CompletedTask;

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
    { _db.Users.Add(user); await _db.SaveChangesAsync(ct); return IdentityResult.Success; }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
    { await _db.SaveChangesAsync(ct); return IdentityResult.Success; }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    { _db.Users.Remove(user); await _db.SaveChangesAsync(ct); return IdentityResult.Success; }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.UserName != null && u.UserName.ToUpper() == normalizedUserName, ct);

    // ---------- IUserPasswordStore ----------
    public Task SetPasswordHashAsync(ApplicationUser u, string? h, CancellationToken ct) { u.PasswordHash = h; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser u, CancellationToken ct)   => Task.FromResult(u.PasswordHash);
    public Task<bool>    HasPasswordAsync(ApplicationUser u, CancellationToken ct)        => Task.FromResult(!string.IsNullOrEmpty(u.PasswordHash));

    // ---------- IUserSecurityStampStore ----------
    public Task SetSecurityStampAsync(ApplicationUser u, string s, CancellationToken ct) { u.SecurityStamp = s; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(ApplicationUser u, CancellationToken ct)  => Task.FromResult(u.SecurityStamp);

    // ---------- IUserLockoutStore ----------
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser u, CancellationToken ct)
        => Task.FromResult<DateTimeOffset?>(u.LockoutEndDateUtc.HasValue
                                              ? new DateTimeOffset(DateTime.SpecifyKind(u.LockoutEndDateUtc.Value, DateTimeKind.Utc))
                                              : null);
    public Task SetLockoutEndDateAsync(ApplicationUser u, DateTimeOffset? d, CancellationToken ct) { u.LockoutEndDateUtc = d?.UtcDateTime; return Task.CompletedTask; }

    public Task<int>  IncrementAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) { u.AccessFailedCount++; return Task.FromResult(u.AccessFailedCount); }
    public Task       ResetAccessFailedCountAsync(ApplicationUser u, CancellationToken ct)     { u.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int>  GetAccessFailedCountAsync(ApplicationUser u, CancellationToken ct)        => Task.FromResult(u.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser u, CancellationToken ct)           => Task.FromResult(u.LockoutEnabled);
    public Task       SetLockoutEnabledAsync(ApplicationUser u, bool enabled, CancellationToken ct) { u.LockoutEnabled = enabled; return Task.CompletedTask; }

    // ---------- IUserEmailStore ----------
    public Task SetEmailAsync(ApplicationUser u, string? e, CancellationToken ct)            { u.Email = e; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(ApplicationUser u, CancellationToken ct)              => Task.FromResult(u.Email);
    public Task<bool>    GetEmailConfirmedAsync(ApplicationUser u, CancellationToken ct)     => Task.FromResult(u.EmailConfirmed);
    public Task          SetEmailConfirmedAsync(ApplicationUser u, bool c, CancellationToken ct) { u.EmailConfirmed = c; return Task.CompletedTask; }
    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToUpper() == normalizedEmail, ct);
    public Task<string?> GetNormalizedEmailAsync(ApplicationUser u, CancellationToken ct)    => Task.FromResult(u.Email?.ToUpperInvariant());
    public Task          SetNormalizedEmailAsync(ApplicationUser u, string? n, CancellationToken ct) => Task.CompletedTask;

    // ---------- IUserPhoneNumberStore ----------
    public Task          SetPhoneNumberAsync(ApplicationUser u, string? p, CancellationToken ct) { u.PhoneNumber = p; return Task.CompletedTask; }
    public Task<string?> GetPhoneNumberAsync(ApplicationUser u, CancellationToken ct)            => Task.FromResult(u.PhoneNumber);
    public Task<bool>    GetPhoneNumberConfirmedAsync(ApplicationUser u, CancellationToken ct)   => Task.FromResult(u.PhoneNumberConfirmed);
    public Task          SetPhoneNumberConfirmedAsync(ApplicationUser u, bool c, CancellationToken ct) { u.PhoneNumberConfirmed = c; return Task.CompletedTask; }

    // ---------- IUserTwoFactorStore ----------
    public Task SetTwoFactorEnabledAsync(ApplicationUser u, bool enabled, CancellationToken ct) { u.TwoFactorEnabled = enabled; return Task.CompletedTask; }
    public Task<bool> GetTwoFactorEnabledAsync(ApplicationUser u, CancellationToken ct)         => Task.FromResult(u.TwoFactorEnabled);

    // ---------- IUserAuthenticatorKeyStore ----------
    public Task SetAuthenticatorKeyAsync(ApplicationUser u, string key, CancellationToken ct) { u.TwoFactorAuthenticatorKey = key; return Task.CompletedTask; }
    public Task<string?> GetAuthenticatorKeyAsync(ApplicationUser u, CancellationToken ct)    => Task.FromResult(u.TwoFactorAuthenticatorKey);
}
