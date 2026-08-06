namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

/// <summary>
/// Resolves privilege grants for a request's role set, backed by
/// dbo.TmX_Role × TmX_Role_Privilege_Mapping × TmX_Privilege.
///
/// The hot path is one preload per request — the
/// <c>[RequiresPrivilege]</c> filter calls
/// <see cref="GetPrivilegesForRolesAsync"/> on the first action it guards,
/// caches the result in <c>HttpContext.Items</c>, and answers all subsequent
/// privilege checks for that request via O(1) hash-set lookup.
/// </summary>
public interface IPrivilegeService
{
    /// <summary>
    /// Returns the distinct set of privilege codes (Privilege_Name) granted to
    /// any of the supplied role names. Empty result on null/empty input.
    /// Comparison is case-insensitive.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetPrivilegesForRolesAsync(
        IEnumerable<string> roleNames,
        CancellationToken   ct = default);

    /// <summary>
    /// Reverse lookup: returns the distinct set of user ids whose roles grant
    /// the supplied privilege — the checker fan-out for notifications. Joins
    /// TmX_User_Role_Mapping × TmX_Role_Privilege_Mapping × TmX_Privilege on
    /// Privilege_Name. Empty result on null/blank input. Active_Flag is not
    /// filtered (same convention as the forward direction).
    /// </summary>
    Task<List<string>> GetUserIdsWithPrivilegeAsync(
        string            privilegeName,
        CancellationToken ct = default);
}
