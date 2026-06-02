using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Delete;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class DeleteFlagHandlerTests
{
    [Fact]
    public async Task SoftDeletes_CustomFlag_AndRetiresIt()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(1, "Custom flag", builtIn: false));
        await db.SaveChangesAsync();

        var handler = new DeleteFlagCommandHandler(db, new FakeCurrentUserService("alice"));
        await handler.Handle(new DeleteFlagCommand(1), default);

        var saved = await db.FlagCatalogues.AsNoTracking().SingleAsync(f => f.FlagId == 1);
        Assert.True(saved.IsDeleted);
        Assert.False(saved.ActiveFlag);          // retired from active reads
        Assert.Equal("alice", saved.DeletedBy);
        Assert.NotNull(saved.DeletedDate);
    }

    [Fact]
    public async Task BuiltInFlag_CannotBeDeleted_ThrowsConflict()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(2, "Shipped flag", builtIn: true));
        await db.SaveChangesAsync();

        var handler = new DeleteFlagCommandHandler(db, new FakeCurrentUserService());

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(new DeleteFlagCommand(2), default));
        Assert.Equal("flag_is_builtin", ex.Code);

        // Untouched.
        var saved = await db.FlagCatalogues.AsNoTracking().SingleAsync(f => f.FlagId == 2);
        Assert.False(saved.IsDeleted);
    }

    [Fact]
    public async Task MissingFlag_ThrowsNotFound()
    {
        await using var db = TestSupport.NewDb();
        var handler = new DeleteFlagCommandHandler(db, new FakeCurrentUserService());

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new DeleteFlagCommand(999), default));
    }

    [Fact]
    public async Task AlreadyDeletedFlag_ThrowsNotFound()
    {
        await using var db = TestSupport.NewDb();
        db.FlagCatalogues.Add(TestSupport.NewFlag(3, "Gone", deleted: true));
        await db.SaveChangesAsync();

        var handler = new DeleteFlagCommandHandler(db, new FakeCurrentUserService());

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new DeleteFlagCommand(3), default));
    }
}
