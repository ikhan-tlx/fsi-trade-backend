using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Create;

/// <summary>
/// Creates a company-branch row. Slice 14 (direct CRUD — no maker/checker
/// approval workflow yet; that's a later increment). Fields mirror the FE
/// BranchFormValues; child mappings (products, role-email, address,
/// parameters) are NOT persisted here yet.
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
        return branch.CompanyBranchId;
    }
}
