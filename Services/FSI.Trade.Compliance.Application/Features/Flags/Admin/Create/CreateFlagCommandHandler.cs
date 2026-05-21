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
            ? await GenerateUniqueCodeAsync(canonicalDescription, req.flagCategoryLkpId, ct)
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
            FlagCategoryLkpId  = req.flagCategoryLkpId,
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
    /// Builds a code in the same shape the seed migration uses:
    ///   <c>"{CATEGORY_HINT}.MRL.{8-char-hex-hash-of-description}"</c>
    /// Category hint resolves from the FLAG_CATEGORY lookup (Hidden_Value),
    /// or "MANUAL" if no category supplied. Hashing the trimmed description
    /// means the same flag-text always maps to the same code.
    /// </summary>
    private async Task<string> GenerateUniqueCodeAsync(
        string description, int? categoryLkpId, CancellationToken ct)
    {
        var categoryPrefix = "MANUAL";
        if (categoryLkpId.HasValue)
        {
            categoryPrefix = await _db.Lookups.AsNoTracking()
                .Where(l => l.Id == categoryLkpId.Value
                         && l.LookupType == "FLAG_CATEGORY")
                .Select(l => l.HiddenValue ?? "MANUAL")
                .FirstOrDefaultAsync(ct) ?? "MANUAL";
        }

        using var sha = SHA1.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(description));
        var shortHex  = Convert.ToHexString(hashBytes).Substring(0, 8);

        return $"{categoryPrefix}.MRL.{shortHex}";
    }
}
