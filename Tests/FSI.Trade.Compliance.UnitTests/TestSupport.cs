using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using FSI.Trade.Compliance.Domain.Entities.Forms;
using FSI.Trade.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// Shared test scaffolding for the Slice 8.1 flag-admin handlers. Each
/// test gets its own isolated in-memory database (unique name) so tests
/// don't bleed state into one another.
/// </summary>
internal static class TestSupport
{
    public static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"flags-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;
        return new ApplicationDbContext(options);
    }

    public static FlagCatalogue NewFlag(
        int id, string name,
        bool builtIn = false, bool deleted = false, bool active = true)
        => new()
        {
            FlagId          = id,
            FlagCode        = $"T.MRL.{id:X8}",
            FlagName        = name,
            FlagDescription = $"{name} description",
            FlagTypeLkpId   = 1,
            ActiveFlag      = active,
            IsBuiltIn       = builtIn,
            IsDeleted       = deleted,
            CreatedBy       = "seed",
            CreatedDate     = DateTime.UtcNow
        };

    public static FlagScope NewScope(
        int id, int flagId, int? productId, int? tabId = null, bool active = true)
        => new()
        {
            FlagScopeId = id,
            FlagId      = flagId,
            ProductId   = productId,
            TabId       = tabId,
            ActiveFlag  = active,
            CreatedBy   = "seed",
            CreatedDate = DateTime.UtcNow
        };

    public static Product NewProduct(int id, string name)
        => new() { ProductId = id, ProductName = name, ActiveFlag = true };

    public static Tab NewTab(int id, string name)
        => new() { TabId = id, TabName = name, ActiveFlag = true, CreatedBy = "seed", CreatedDate = DateTime.UtcNow };
}

/// <summary>Deterministic ICurrentUserService for handler tests.</summary>
internal sealed class FakeCurrentUserService : ICurrentUserService
{
    public FakeCurrentUserService(string? userId = "test-user") => UserId = userId;

    public string? UserId          { get; }
    public string? UserName        => "Test User";
    public bool    IsAuthenticated => UserId is not null;
    public string? IpAddress       => "127.0.0.1";
    public int?    TenantId        => 245;
}
