using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Features.FieldSetup.Common;
using FSI.Trade.Compliance.Application.Features.FieldSetup.PublishSection;
using FSI.Trade.Compliance.Domain.Entities.Forms;
using FSI.Trade.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class FieldTypeSafeMatrixTests
{
    [Theory]
    [InlineData(3, 4, true)]   // Text → Long text
    [InlineData(4, 3, true)]   // Long text → Text
    [InlineData(3, 3, true)]   // no-op
    [InlineData(3, 55, true)]  // Text → Number   (scalar family)
    [InlineData(3, 2, true)]   // Text → Date     (scalar family)
    [InlineData(55, 3, true)]  // Number → Text   (scalar family)
    [InlineData(2, 4, true)]   // Date → Long text(scalar family)
    [InlineData(55, 2, true)]  // Number → Date   (scalar family)
    [InlineData(3, 25, false)] // Text → Dropdown (unsafe — lookup)
    [InlineData(25, 3, false)] // Dropdown → Text (unsafe — lookup)
    [InlineData(30, 4, false)] // Section → Long text (unsafe — structural)
    [InlineData(27, 3, false)] // Table → Text    (unsafe — structural)
    public void IsSafeConversion_AllowsScalarFamily(int from, int to, bool expected)
        => Assert.Equal(expected, FieldTypeSafeMatrix.IsSafeConversion(from, to));

    [Theory]
    [InlineData(3, true)]   // Text
    [InlineData(4, true)]   // Long text
    [InlineData(55, true)]  // Number
    [InlineData(2, true)]   // Date
    [InlineData(25, false)] // Dropdown — locked
    [InlineData(27, false)] // Table — locked
    [InlineData(null, false)]
    public void IsConvertible_TrueForScalarFamily(int? lkp, bool expected)
        => Assert.Equal(expected, FieldTypeSafeMatrix.IsConvertible(lkp));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", false)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("0", false)]
    [InlineData("tf204 == '1'", true)]
    [InlineData("tf1 && tf2", true)]
    public void IsFormula_DetectsExpressions(string? expr, bool expected)
        => Assert.Equal(expected, FieldTypeSafeMatrix.IsFormula(expr));

    [Fact]
    public void IsPlatformManaged_TrueWhenAnyExpressionIsFormula()
    {
        Assert.False(FieldTypeSafeMatrix.IsPlatformManaged("true", "false", null));
        Assert.True(FieldTypeSafeMatrix.IsPlatformManaged(null, "tf5 == '2'", null));
        Assert.True(FieldTypeSafeMatrix.IsPlatformManaged("tf9 != ''", null, null));
    }
}

public class PublishSectionHandlerTests
{
    private static PublishSectionCommandHandler Handler(ApplicationDbContext db)
        => new(db, new FakeCurrentUserService("admin"));

    private static TenantFieldSetup Field(
        int id, int productId, int typeLkp, int seq,
        string? lookupType = null, string? isMandatory = null,
        int? parentId = null)
        => new()
        {
            TenantFieldSetupId       = id,
            ProductId                = productId,
            ParentTenantFieldSetupId = parentId,
            FieldName                = $"F{id}",
            FieldLabel               = $"Field {id}",
            FieldTypeLkp             = typeLkp,
            FieldSequence            = seq,
            FieldLookupType          = lookupType,
            IsMandatory              = isMandatory,
            IsActive                 = true,
            CreatedBy                = "seed",
            CreatedDate              = DateTime.UtcNow,
        };

    [Fact]
    public async Task Reorder_RewritesFieldSequenceWithGapsOfTen()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.AddRange(
            Field(1, 100, 3, 10),
            Field(2, 100, 3, 20),
            Field(3, 100, 3, 30));
        await db.SaveChangesAsync();

        // Publish reversed display order: 3, 2, 1.
        var result = await Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new()
            {
                new PublishSectionItem { tenantFieldSetupId = 3, order = 0 },
                new PublishSectionItem { tenantFieldSetupId = 2, order = 1 },
                new PublishSectionItem { tenantFieldSetupId = 1, order = 2 },
            }
        }, default);

        Assert.Equal(3, result.reordered);
        Assert.Equal(10, (await db.TenantFieldSetups.AsNoTracking().SingleAsync(f => f.TenantFieldSetupId == 3)).FieldSequence);
        Assert.Equal(20, (await db.TenantFieldSetups.AsNoTracking().SingleAsync(f => f.TenantFieldSetupId == 2)).FieldSequence);
        Assert.Equal(30, (await db.TenantFieldSetups.AsNoTracking().SingleAsync(f => f.TenantFieldSetupId == 1)).FieldSequence);
    }

    [Fact]
    public async Task SafeTypeChange_TextToLongText_IsApplied()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.Add(Field(1, 100, 3, 10)); // Text
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new() { new PublishSectionItem { tenantFieldSetupId = 1, order = 0, fieldTypeLkp = 4 } }
        }, default);

        Assert.Equal(1, result.retyped);
        Assert.Equal(4, (await db.TenantFieldSetups.AsNoTracking().SingleAsync(f => f.TenantFieldSetupId == 1)).FieldTypeLkp);
    }

    [Fact]
    public async Task UnsafeTypeChange_TextToDropdown_Throws()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.Add(Field(1, 100, 3, 10));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new() { new PublishSectionItem { tenantFieldSetupId = 1, order = 0, fieldTypeLkp = 25 } }
        }, default));
        Assert.Contains("unsafe_type_change", ex.Message + ex.GetType().Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TypeChange_OnLookupBoundField_Throws()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.Add(Field(1, 100, 3, 10, lookupType: "Country"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new() { new PublishSectionItem { tenantFieldSetupId = 1, order = 0, fieldTypeLkp = 4 } }
        }, default));
    }

    [Fact]
    public async Task RequiredToggle_OnFormulaField_Throws()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.Add(Field(1, 100, 3, 10, isMandatory: "tf204 == '1'"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new() { new PublishSectionItem { tenantFieldSetupId = 1, order = 0, isMandatory = true } }
        }, default));
    }

    [Fact]
    public async Task ProductMismatch_Throws()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.Add(Field(1, 999, 3, 10)); // belongs to product 999
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new() { new PublishSectionItem { tenantFieldSetupId = 1, order = 0 } }
        }, default));
    }

    [Fact]
    public async Task NewField_NameCollidingWithAnotherSubProduct_IsMintedUnique()
    {
        await using var db = TestSupport.NewDb();
        // Tab 10 (sub-product A): an existing field already named "ABC_Quantity".
        db.TenantFieldSetups.Add(new TenantFieldSetup
        {
            TenantFieldSetupId = 1, ProductId = 100, TabId = 10,
            FieldName = "ABC_Quantity", FieldLabel = "Quantity", FieldTypeLkp = 3,
            FieldSequence = 10, IsActive = true, CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
        });
        // Tab 20 (sub-product B): its most-common existing prefix is "ABC".
        db.TenantFieldSetups.Add(new TenantFieldSetup
        {
            TenantFieldSetupId = 2, ProductId = 100, TabId = 20,
            FieldName = "ABC_Amount", FieldLabel = "Amount", FieldTypeLkp = 3,
            FieldSequence = 10, IsActive = true, CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // Add a NEW "Quantity" field under tab 20 → the generator would mint "ABC_Quantity"
        // (tab-20 prefix + label), which already exists under tab 10. With product-wide
        // uniqueness it must be suffixed instead of duplicated across the two sub-products.
        var result = await Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            tabId = 20,
            items = new()
            {
                new PublishSectionItem { isNew = true, tempId = 1, order = 0, fieldTypeLkp = 3, fieldLabel = "Quantity" },
            }
        }, default);

        Assert.Equal(1, result.inserted);
        var names = await db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId == 100).Select(f => f.FieldName).ToListAsync();
        // Exactly one "ABC_Quantity" survives (the tab-10 original) — the new one is distinct.
        Assert.Equal(1, names.Count(n => n == "ABC_Quantity"));
        var created = await db.TenantFieldSetups.AsNoTracking()
            .SingleAsync(f => f.TabId == 20 && f.FieldLabel == "Quantity");
        Assert.NotEqual("ABC_Quantity", created.FieldName);
        Assert.StartsWith("ABC_Quantity", created.FieldName); // e.g. "ABC_Quantity_2"
    }

    [Fact]
    public async Task NewField_ExplicitNameDuplicateInAnotherSubProduct_IsRejected()
    {
        await using var db = TestSupport.NewDb();
        // Tab 10 already owns "DUP_Name".
        db.TenantFieldSetups.Add(new TenantFieldSetup
        {
            TenantFieldSetupId = 1, ProductId = 100, TabId = 10,
            FieldName = "DUP_Name", FieldLabel = "Existing", FieldTypeLkp = 3,
            FieldSequence = 10, IsActive = true, CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
        });
        // Tab 20 has a field so the tab/template resolves.
        db.TenantFieldSetups.Add(new TenantFieldSetup
        {
            TenantFieldSetupId = 2, ProductId = 100, TabId = 20,
            FieldName = "ABC_Foo", FieldLabel = "Foo", FieldTypeLkp = 3,
            FieldSequence = 10, IsActive = true, CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // Add a NEW field under tab 20 with an EXPLICIT name that already exists in tab 10.
        // Field_Name uniqueness is a hard primitive → this must be rejected (409), not renamed.
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100, tabId = 20,
            items = new()
            {
                new PublishSectionItem { isNew = true, tempId = 1, order = 0, fieldTypeLkp = 3,
                    fieldLabel = "Whatever", fieldName = "DUP_Name" },
            }
        }, default));
        Assert.Equal("field_name_duplicate", ex.Code);
        // The original is untouched and no second row was created.
        Assert.Equal(2, await db.TenantFieldSetups.CountAsync());
    }

    [Fact]
    public async Task LabelAndVisible_AreApplied()
    {
        await using var db = TestSupport.NewDb();
        db.TenantFieldSetups.Add(Field(1, 100, 3, 10));
        await db.SaveChangesAsync();

        await Handler(db).Handle(new PublishSectionCommand
        {
            productId = 100,
            items = new() { new PublishSectionItem { tenantFieldSetupId = 1, order = 0, fieldLabel = "Renamed", isActive = false } }
        }, default);

        var e = await db.TenantFieldSetups.AsNoTracking().SingleAsync(f => f.TenantFieldSetupId == 1);
        Assert.Equal("Renamed", e.FieldLabel);
        Assert.False(e.IsActive);
    }
}
