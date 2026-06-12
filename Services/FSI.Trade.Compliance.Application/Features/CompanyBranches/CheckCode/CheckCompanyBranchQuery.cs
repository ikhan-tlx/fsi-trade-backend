using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.CheckCode;

/// <summary>True when a branch with the given code already exists. Slice 14.</summary>
public record CheckCompanyBranchQuery(string Code) : IRequest<bool>;

public class CheckCompanyBranchQueryHandler : IRequestHandler<CheckCompanyBranchQuery, bool>
{
    private readonly IApplicationDbContext _db;
    public CheckCompanyBranchQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<bool> Handle(CheckCompanyBranchQuery req, CancellationToken ct)
    {
        var code = (req.Code ?? "").Trim();
        if (code.Length == 0) return false;
        return await _db.CompanyBranches.AsNoTracking().AnyAsync(b => b.BranchCode == code, ct);
    }
}
