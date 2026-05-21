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

        // Flag must exist.
        var flagExists = await _db.FlagCatalogues
            .AnyAsync(c => c.FlagId == req.flagId, ct);
        if (!flagExists)
            throw new NotFoundException("flag_not_found",
                $"Flag {req.flagId} does not exist.");

        // Reject duplicates upfront so we return a clean 409 instead of
        // a UNIQUE-index violation surfaced as 500. The filtered index
        // on (Flag_ID, Product_ID, Tab_ID) handles NULL tab via a
        // separate index — see migration 2026_05_011.
        var duplicate = await _db.FlagScopes
            .Where(s => s.FlagId    == req.flagId
                     && s.ProductId == req.productId
                     && s.TabId     == req.tabId)
            .AnyAsync(ct);
        if (duplicate)
            throw new ConflictException("flag_scope_exists",
                $"Flag {req.flagId} already has a scope for Product {req.productId}"
                + (req.tabId.HasValue ? $", Tab {req.tabId}." : " (product-level)."));

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
