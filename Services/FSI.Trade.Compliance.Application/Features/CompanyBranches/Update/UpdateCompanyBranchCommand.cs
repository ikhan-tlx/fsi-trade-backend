using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Update;

/// <summary>Updates the mutable fields of a company branch. Slice 14.</summary>
public class UpdateCompanyBranchCommand : IRequest<Unit>
{
    public int       CompanyBranchId    { get; set; }   // from route
    public string?   BranchName         { get; set; }
    public string?   BranchDescription  { get; set; }
    public int?      LocationId         { get; set; }
    public string?   Status             { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
}

public class UpdateCompanyBranchCommandHandler : IRequestHandler<UpdateCompanyBranchCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public UpdateCompanyBranchCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<Unit> Handle(UpdateCompanyBranchCommand req, CancellationToken ct)
    {
        var branch = await _db.CompanyBranches
            .FirstOrDefaultAsync(b => b.CompanyBranchId == req.CompanyBranchId, ct)
            ?? throw new NotFoundException("branch_not_found", $"Branch {req.CompanyBranchId} not found.");

        if (req.BranchName != null)        branch.BranchName        = req.BranchName.Trim();
        if (req.BranchDescription != null) branch.BranchDescription = req.BranchDescription.Trim();
        if (req.LocationId != null)        branch.LocationId        = req.LocationId;
        if (!string.IsNullOrWhiteSpace(req.Status)) branch.Status   = req.Status;
        if (req.EffectiveStartDate != null) branch.EffectiveStartDate = req.EffectiveStartDate.Value;
        if (req.EffectiveEndDate != null)   branch.EffectiveEndDate   = req.EffectiveEndDate.Value;

        branch.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        branch.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
