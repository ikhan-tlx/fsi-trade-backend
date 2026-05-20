namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

/// <summary>
/// Reads role information for a user. Slice 1 only needs role NAMES (for the JWT
/// `role` claim). Anything more elaborate (role CRUD, privilege expansion) belongs
/// to a different port and lands in slice 2.
/// </summary>
public interface IRoleQueryService
{
    Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken ct = default);
}
