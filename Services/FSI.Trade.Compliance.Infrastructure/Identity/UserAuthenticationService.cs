using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

/// <summary>
/// Thin adapter over ASP.NET Core Identity's <see cref="UserManager{TUser}"/>.
/// Each method maps one Application primitive to its UserManager equivalent.
/// No use-case orchestration here — that lives in MediatR handlers.
/// </summary>
public class UserAuthenticationService : IUserAuthenticationService
{
    private readonly UserManager<ApplicationUser> _users;
    public UserAuthenticationService(UserManager<ApplicationUser> users) => _users = users;

    public Task<ApplicationUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => _users.FindByNameAsync(username);

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct = default)
        => _users.FindByIdAsync(userId);

    public Task<bool> CheckPasswordAsync(ApplicationUser user, string password, CancellationToken ct = default)
        => _users.CheckPasswordAsync(user, password);

    public async Task RecordFailedAccessAsync(ApplicationUser user, CancellationToken ct = default)
        => await _users.AccessFailedAsync(user);

    public async Task ResetFailedAccessAsync(ApplicationUser user, CancellationToken ct = default)
        => await _users.ResetAccessFailedCountAsync(user);

    public Task<bool> IsLockedOutAsync(ApplicationUser user, CancellationToken ct = default)
        => _users.IsLockedOutAsync(user);

    public async Task UpdateUserAsync(ApplicationUser user, CancellationToken ct = default)
        => await _users.UpdateAsync(user);

    public async Task<(bool ok, IReadOnlyList<string> errors)> ChangePasswordAsync(
        ApplicationUser user, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var result = await _users.ChangePasswordAsync(user, oldPassword, newPassword);
        if (result.Succeeded) return (true, Array.Empty<string>());
        return (false, result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<(bool ok, IReadOnlyList<string> errors)> CreateUserAsync(
        ApplicationUser user, string password, CancellationToken ct = default)
    {
        var result = await _users.CreateAsync(user, password);
        if (result.Succeeded) return (true, Array.Empty<string>());
        return (false, result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task UnlockAsync(ApplicationUser user, CancellationToken ct = default)
    {
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
    }
}
