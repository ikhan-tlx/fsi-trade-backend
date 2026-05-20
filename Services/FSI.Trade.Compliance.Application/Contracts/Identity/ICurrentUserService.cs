namespace FSI.Trade.Compliance.Application.Contracts.Identity;

public interface ICurrentUserService
{
    string? UserId           { get; }
    string? UserName         { get; }
    bool    IsAuthenticated  { get; }
    string? IpAddress        { get; }

    /// <summary>
    /// Tenant ID parsed from the JWT's <c>tenantId</c> claim. Null if unauthenticated
    /// or if the claim is malformed. Used by tenant-scoped queries (Configurations,
    /// Lookups) to filter to the caller's tenant.
    /// </summary>
    int?    TenantId         { get; }
}
