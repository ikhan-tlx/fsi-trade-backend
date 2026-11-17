using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Common;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Create;

/// <summary>
/// Creates a company-branch row (replayed on maker-checker approval of a Branch
/// request). Fields mirror the FE BranchFormValues; the branch→product
/// entitlement mappings are persisted (<see cref="TmxBranchProductsMapping"/> →
/// <c>TmX_Branch_Products_Mapping</c>) so the wizard's branch-first product step
/// shows this branch's products. Other child mappings (role-email, address,
/// parameters) are not persisted here yet.
/// </summary>
public class CreateCompanyBranchCommand : IRequest<int>
{
    public string    BranchCode         { get; set; } = "";
    public string?   BranchName         { get; set; }
    public string?   BranchDescription  { get; set; }
    public int       CompanyId          { get; set; }
    public int?      LocationId         { get; set; }
    public string?   Status             { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
    /// <summary>Products this branch may transact (FE field name preserved for JSON binding).</summary>
    public List<BranchProductItemDto>? TmxBranchProductsMapping { get; set; }
}

public class CreateCompanyBranchCommandHandler : IRequestHandler<CreateCompanyBranchCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public CreateCompanyBranchCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<int> Handle(CreateCompanyBranchCommand req, CancellationToken ct)
    {
        var code = (req.BranchCode ?? "").Trim();
        if (code.Length == 0)
            throw new ConflictException("branch_code_required", "Branch code is required.");

        var exists = await _db.CompanyBranches.AsNoTracking()
            .AnyAsync(b => b.BranchCode == code, ct);
        if (exists)
            throw new ConflictException("branch_code_taken", $"Branch code '{code}' already exists.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var branch = new CompanyBranch
        {
            TenantId           = _current.TenantId ?? 1,
            CompanyId          = req.CompanyId,
            BranchCode         = code,
            BranchName         = req.BranchName?.Trim(),
            BranchDescription  = req.BranchDescription?.Trim(),
            LocationId         = req.LocationId,
            Status             = string.IsNullOrWhiteSpace(req.Status) ? "Active" : req.Status,
            ActiveFlag         = true,
            EffectiveStartDate = req.EffectiveStartDate ?? DateTime.UtcNow,
            EffectiveEndDate   = req.EffectiveEndDate ?? new DateTime(9999, 12, 31),
            CreatedBy          = actor,
            CreatedDate        = DateTime.UtcNow,
        };

        _db.CompanyBranches.Add(branch);
        await _db.SaveChangesAsync(ct);

        // Persist the branch's product entitlements (branch → product).
        await BranchProductSync.ApplyAsync(_db, branch.CompanyBranchId, branch.TenantId, req.TmxBranchProductsMapping, ct);

        return branch.CompanyBranchId;
    }
}
