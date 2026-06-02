using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

public class AddFlagScopeCommandHandler : IRequestHandler<AddFlagScopeCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public AddFlagScopeCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<int> Handle(AddFlagScopeCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Adding a flag scope requires an authenticated caller.");

        // Flag must exist and not be soft-deleted.
        var flagExists = await _db.FlagCatalogues
            .AnyAsync(c => c.FlagId == req.flagId && !c.IsDeleted, ct);
        if (!flagExists)
            throw new NotFoundException("flag_not_found",
                $"Flag {req.flagId} does not exist.");

        // Reject duplicates upfront so we return a clean 409 instead of a
        // UNIQUE-index violation surfaced as 500. Branch the null cases in
        // C# rather than `== req.productId` so EF emits `IS NULL` (a null
        // parameter via `==` would generate `= @p` and never match a NULL).
        // The three filtered unique indexes (product+tab / product-level /
        // all-products) back these combinations — see migration 2026_06_031.
        IQueryable<FlagScope> dupQuery = _db.FlagScopes.Where(s => s.FlagId == req.flagId);
        dupQuery = req.productId.HasValue
            ? dupQuery.Where(s => s.ProductId == req.productId.Value)
            : dupQuery.Where(s => s.ProductId == null);
        dupQuery = req.tabId.HasValue
            ? dupQuery.Where(s => s.TabId == req.tabId.Value)
            : dupQuery.Where(s => s.TabId == null);

        if (await dupQuery.AnyAsync(ct))
            throw new ConflictException("flag_scope_exists",
                $"Flag {req.flagId} already has "
                + (req.productId.HasValue
                    ? $"a scope for Product {req.productId}"
                      + (req.tabId.HasValue ? $", Tab {req.tabId}." : " (product-level).")
                    : "an all-products scope."));

        var now    = DateTime.UtcNow;
        var entity = new FlagScope
        {
            FlagId       = req.flagId,
            ProductId    = req.productId,
            TabId        = req.tabId,
            SortOrder    = req.sortOrder  ?? 0,
            ActiveFlag   = req.activeFlag ?? true,
            CreatedBy    = userId,
            CreatedDate  = now,
        };

        _db.FlagScopes.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.FlagScopeId;
    }
}
