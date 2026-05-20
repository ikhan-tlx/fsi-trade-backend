using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Generic primitives for user-credential operations. Each method does ONE thing
/// and returns a shape Application can compose. No use-case specialization
/// (no "LoginUser" / "DoChangePasswordFlow") — that orchestration lives in
/// MediatR handlers.
/// </summary>
public interface IUserAuthenticationService
{
    Task<ApplicationUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct = default);

    Task<bool>             CheckPasswordAsync(ApplicationUser user, string password, CancellationToken ct = default);
    Task                   RecordFailedAccessAsync(ApplicationUser user, CancellationToken ct = default);
    Task                   ResetFailedAccessAsync(ApplicationUser user, CancellationToken ct = default);
    Task<bool>             IsLockedOutAsync(ApplicationUser user, CancellationToken ct = default);

    Task                   UpdateUserAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>
    /// Verifies old password and sets the new one. Generic result tuple — caller
    /// decides whether errors become exceptions, ProblemDetails, or anything else.
    /// </summary>
    Task<(bool ok, IReadOnlyList<string> errors)>
                           ChangePasswordAsync(ApplicationUser user, string oldPassword, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// Creates a new user with an initial password. Hashes the password (V3),
    /// inserts into TmX_User. Generic result tuple — caller composes errors
    /// into the appropriate exception.
    /// </summary>
    Task<(bool ok, IReadOnlyList<string> errors)>
                           CreateUserAsync(ApplicationUser user, string password, CancellationToken ct = default);

    /// <summary>
    /// Releases a locked-out account: clears LockoutEndDateUtc and resets
    /// AccessFailedCount to 0.
    /// </summary>
    Task                   UnlockAsync(ApplicationUser user, CancellationToken ct = default);
}
