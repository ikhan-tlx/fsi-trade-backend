using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class AddFlagScopeHandlerTests
{
    private static AddFlagScopeCommandHandler Handler(Infrastructure.Persistence.ApplicationDbContext db)
        => new(db, new FakeCurrentUserService("bob"));

    [Fact]
    public async Task AllProductsScope_NullProduct_IsCreated()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Routing through opaque jurisdiction"));
        await db.SaveChangesAsync();

        var newId = await Handler(db).Handle(
            new AddFlagScopeCommand { flagId = 1, productId = null, tabId = null }, default);

        var scope = await db.FlagScopes.AsNoTracking().SingleAsync(s => s.FlagScopeId == newId);
        Assert.Null(scope.ProductId);     // global
        Assert.Null(scope.TabId);
        Assert.Equal(1, scope.FlagId);
        Assert.Equal("bob", scope.CreatedBy);
    }

    [Fact]
    public async Task ProductScope_IsCreated()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Goods inconsistent with HS"));
        await db.SaveChangesAsync();

        var newId = await Handler(db).Handle(
            new AddFlagScopeCommand { flagId = 1, productId = 7, tabId = 12 }, default);

        var scope = await db.FlagScopes.AsNoTracking().SingleAsync(s => s.FlagScopeId == newId);
        Assert.Equal(7, scope.ProductId);
        Assert.Equal(12, scope.TabId);
    }

    [Fact]
    public async Task DuplicateGlobalScope_ThrowsConflict()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Flag"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null, tabId: null));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => Handler(db).Handle(new AddFlagScopeCommand { flagId = 1, productId = null, tabId = null }, default));
        Assert.Equal("flag_scope_exists", ex.Code);
    }

    [Fact]
    public async Task DuplicateProductScope_ThrowsConflict()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Flag"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7, tabId: null));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => Handler(db).Handle(new AddFlagScopeCommand { flagId = 1, productId = 7, tabId = null }, default));
    }

    [Fact]
    public async Task GlobalAndProductScope_CoexistForSameFlag()
    {
        // A global scope must NOT collide with a product-level scope on the
        // same flag — different tiers, both allowed.
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Flag"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null, tabId: null));
        await db.SaveChangesAsync();

        var newId = await Handler(db).Handle(
            new AddFlagScopeCommand { flagId = 1, productId = 7, tabId = null }, default);

        Assert.True(newId > 0);
        Assert.Equal(2, await db.FlagScopes.CountAsync(s => s.FlagId == 1));
    }

    [Fact]
    public async Task DeletedFlag_ThrowsNotFound()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Gone", deleted: true));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => Handler(db).Handle(new AddFlagScopeCommand { flagId = 1, productId = 7 }, default));
    }
}
