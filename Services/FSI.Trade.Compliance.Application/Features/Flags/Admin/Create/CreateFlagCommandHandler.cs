using System.Security.Cryptography;
using System.Text;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Create;

public class CreateFlagCommandHandler : IRequestHandler<CreateFlagCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public CreateFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<int> Handle(CreateFlagCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException("unauthenticated",
                "Flag creation requires an authenticated caller.");

        var now = DateTime.UtcNow;
        var canonicalDescription = req.flagDescription.Trim();

        // Resolve / generate Flag_Code.
        var code = string.IsNullOrWhiteSpace(req.flagCode)
            ? GenerateCode(canonicalDescription)
            : req.flagCode!.Trim();

        // Uniqueness check (the DB index would catch it, but a clean
        // ConflictException is friendlier than a 500 with a UNIQUE violation).
        var codeTaken = await _db.FlagCatalogues
            .AnyAsync(c => c.FlagCode == code, ct);
        if (codeTaken)
            throw new ConflictException("flag_code_taken",
                $"Flag code '{code}' is already in use.");

        var entity = new FlagCatalogue
        {
            FlagCode           = code,
            FlagName           = req.flagName.Trim(),
            FlagDescription    = canonicalDescription,
            FlagTypeLkpId      = req.flagTypeLkpId,
            SeverityLkpId      = req.severityLkpId,
            DefaultWeight      = req.defaultWeight ?? 1.00m,
            RequiresEvidence   = req.requiresEvidence,
            SourceSystem       = string.IsNullOrWhiteSpace(req.sourceSystem) ? null : req.sourceSystem!.Trim(),
            ActiveFlag         = true,
            CreatedBy          = userId,
            CreatedDate        = now,
        };

        _db.FlagCatalogues.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.FlagId;
    }

    /// <summary>
    /// Builds a stable code: <c>"MANUAL.MRL.{8-char-hex-hash-of-description}"</c>.
    /// Hashing the trimmed description means the same flag-text always maps to
    /// the same code. (Category-derived prefixes were dropped 2026-06-01 along
    /// with the flag-level category.)
    /// </summary>
    private static string GenerateCode(string description)
    {
        using var sha = SHA1.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(description));
        var shortHex  = Convert.ToHexString(hashBytes).Substring(0, 8);

        return $"MANUAL.MRL.{shortHex}";
    }
}
