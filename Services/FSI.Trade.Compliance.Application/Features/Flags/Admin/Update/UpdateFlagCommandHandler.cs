using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Update;

public class UpdateFlagCommandHandler : IRequestHandler<UpdateFlagCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<int> Handle(UpdateFlagCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Flag update requires an authenticated caller.");

        var entity = await _db.FlagCatalogues
            .FirstOrDefaultAsync(c => c.FlagId == req.flagId, ct)
            ?? throw new NotFoundException("flag_not_found",
                $"Flag {req.flagId} does not exist.");

        entity.FlagName           = req.flagName.Trim();
        entity.FlagDescription    = req.flagDescription.Trim();
        entity.FlagTypeLkpId      = req.flagTypeLkpId;
        entity.FlagCategoryLkpId  = req.flagCategoryLkpId;
        entity.SeverityLkpId      = req.severityLkpId;
        entity.DefaultWeight      = req.defaultWeight ?? entity.DefaultWeight;
        entity.RequiresEvidence   = req.requiresEvidence;
        entity.SourceSystem       = string.IsNullOrWhiteSpace(req.sourceSystem) ? null : req.sourceSystem!.Trim();
        entity.LastUpdatedBy      = userId;
        entity.LastUpdatedDate    = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return entity.FlagId;
    }
}
