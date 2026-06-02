using FSI.Trade.Compliance.Application.Features.Flags.Read;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// Covers the read path behind GET /Flag/Product/{productId} (and the
/// transaction flag panel): product-specific scopes PLUS "all products"
/// (global) scopes must surface for a given product.
/// </summary>
public class FlagScopeProjectionTests
{
    [Fact]
    public async Task ProductQuery_IncludesGlobalAndProductScopes()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Global flag"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Product 7 flag"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null));   // all products
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7));
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadByProductAsync(db, productId: 7, tabId: null, default);

        var flagIds = rows.Select(r => r.flagId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 2 }, flagIds);
        Assert.Contains(rows, r => r.flagId == 1 && r.productId == null);   // global
    }

    [Fact]
    public async Task ProductQuery_ExcludesOtherProductScopes()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(3, "Product 99 flag"));
        db.FlagScopes.Add(TestSupport.NewScope(52, flagId: 3, productId: 99));
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadByProductAsync(db, productId: 7, tabId: null, default);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task InactiveScope_IsExcluded()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Flag"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7, active: false));
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadByProductAsync(db, productId: 7, tabId: null, default);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task DeletedOrInactiveCatalogue_IsExcluded()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Deleted", deleted: true));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Inactive", active: false));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7));
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7));
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadByProductAsync(db, productId: 7, tabId: null, default);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task TabFilter_KeepsGlobalScopesRegardlessOfTab()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Global"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Tab 12"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(3, "Tab 99"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: null));            // global
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7, tabId: 12));
        db.FlagScopes.Add(TestSupport.NewScope(52, flagId: 3, productId: 7, tabId: 99));
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadByProductAsync(db, productId: 7, tabId: 12, default);

        var flagIds = rows.Select(r => r.flagId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 2 }, flagIds);   // global + the tab-12 flag; not tab-99
    }
}
