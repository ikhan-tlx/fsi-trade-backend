using FSI.Trade.Compliance.Application.Features.Flags.Admin.List;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class ListFlagCatalogueHandlerTests
{
    [Fact]
    public async Task ExcludesSoftDeletedFlags()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Live"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Deleted", deleted: true));
        await db.SaveChangesAsync();

        var result = await new ListFlagCatalogueQueryHandler(db)
            .Handle(new ListFlagCatalogueQuery(), default);

        Assert.Single(result.items);
        Assert.Equal(1, result.items[0].flagId);
    }

    [Fact]
    public async Task ProjectsIsBuiltInAndScopes_WithResolvedNames()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Goods inconsistent with HS", builtIn: true));
        db.Products.Add(TestSupport.NewProduct(7, "Import transaction"));
        db.Tabs.Add(TestSupport.NewTab(12, "Import LC Issuance"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7, tabId: 12));
        await db.SaveChangesAsync();

        var result = await new ListFlagCatalogueQueryHandler(db)
            .Handle(new ListFlagCatalogueQuery(), default);

        var item = Assert.Single(result.items);
        Assert.True(item.isBuiltIn);
        var scope = Assert.Single(item.scopes);
        Assert.Equal(7, scope.productId);
        Assert.Equal("Import transaction", scope.productName);
        Assert.Equal(12, scope.tabId);
        Assert.Equal("Import LC Issuance", scope.tabName);
    }

    [Fact]
    public async Task GlobalScope_ResolvesNullProductName()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Cash-equivalent settlement"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null, tabId: null));
        await db.SaveChangesAsync();

        var result = await new ListFlagCatalogueQueryHandler(db)
            .Handle(new ListFlagCatalogueQuery(), default);

        var scope = Assert.Single(result.items[0].scopes);
        Assert.Null(scope.productId);
        Assert.Null(scope.productName);   // FE renders "All products"
    }

    [Fact]
    public async Task GlobalScopeOnlyFilter_ReturnsOnlyGloballyScopedFlags()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Global flag"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Product flag"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null));
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7));
        await db.SaveChangesAsync();

        var result = await new ListFlagCatalogueQueryHandler(db)
            .Handle(new ListFlagCatalogueQuery { GlobalScopeOnly = true }, default);

        Assert.Single(result.items);
        Assert.Equal(1, result.items[0].flagId);
    }

    [Fact]
    public async Task ProductIdFilter_IncludesGlobalAndProductScopes_ExcludesOtherProduct()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Global"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "On product 7"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(3, "On product 99"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null));
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7));
        db.FlagScopes.Add(TestSupport.NewScope(52, flagId: 3, productId: 99));
        await db.SaveChangesAsync();

        var result = await new ListFlagCatalogueQueryHandler(db)
            .Handle(new ListFlagCatalogueQuery { ProductId = 7 }, default);

        var ids = result.items.Select(i => i.flagId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 2 }, ids);
    }
}
