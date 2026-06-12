using FSI.Trade.Compliance.Application.Features.Flags.Read;
using FSI.Trade.Compliance.Domain.Entities.Flags;
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

    // ---- LoadSetForTransactionAsync: only the SET flags, not the catalogue ----

    [Fact]
    public async Task SetForTransaction_ReturnsOnlySelectedFlags_NotWholeScopeCatalogue()
    {
        await using var db = TestSupport.NewDb();
        // Three flags are applicable to product 7 (would all appear in the picker)…
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Applicable A"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Applicable B"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(3, "Selected C"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7, tabId: 6));
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7, tabId: 6));
        db.FlagScopes.Add(TestSupport.NewScope(52, flagId: 3, productId: 7, tabId: 6));
        // …but only flag 3 is actually SET on transaction 7773.
        db.TransactionFlags.Add(new TransactionFlag
        {
            TransactionFlagId = 900,
            TransactionId     = 7773,
            FlagId            = 3,
            IsFlagged         = true,
            SetBy             = "analyst@icbc.com",
            SetDate           = DateTime.UtcNow,
            CreatedBy         = "analyst@icbc.com",
            CreatedDate       = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadSetForTransactionAsync(db, transactionId: 7773, productId: 7, default);

        var flag = Assert.Single(rows);
        Assert.Equal(3, flag.flagId);
        Assert.Equal(900, flag.transactionFlagId);
        Assert.True(flag.isFlagged);
        Assert.Equal(6, flag.tabId);            // best-effort scope context attached
    }

    [Fact]
    public async Task SetForTransaction_ExcludesToggledOffFlags()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Kept"));
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "ToggledOff"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7, tabId: 6));
        db.FlagScopes.Add(TestSupport.NewScope(51, flagId: 2, productId: 7, tabId: 6));
        db.TransactionFlags.Add(new TransactionFlag
        {
            TransactionFlagId = 901, TransactionId = 7773, FlagId = 1, IsFlagged = true,
            SetBy = "u", SetDate = DateTime.UtcNow, CreatedBy = "u", CreatedDate = DateTime.UtcNow
        });
        db.TransactionFlags.Add(new TransactionFlag
        {
            TransactionFlagId = 902, TransactionId = 7773, FlagId = 2, IsFlagged = false, // un-flagged
            SetBy = "u", SetDate = DateTime.UtcNow, CreatedBy = "u", CreatedDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadSetForTransactionAsync(db, transactionId: 7773, productId: 7, default);

        var flag = Assert.Single(rows);   // only the IsFlagged=true row
        Assert.Equal(1, flag.flagId);
    }

    [Fact]
    public async Task SetForTransaction_NoSelections_ReturnsEmpty()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Applicable"));
        db.FlagScopes.Add(TestSupport.NewScope(50, flagId: 1, productId: 7, tabId: 6));
        await db.SaveChangesAsync();

        var rows = await TransactionFlagProjection.LoadSetForTransactionAsync(db, transactionId: 7773, productId: 7, default);

        Assert.Empty(rows); // nothing set → no flags, even though one is applicable
    }
}
