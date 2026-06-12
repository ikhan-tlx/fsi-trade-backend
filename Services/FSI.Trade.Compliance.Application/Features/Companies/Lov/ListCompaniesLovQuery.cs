using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Companies.Lov;

/// <summary>
/// Companies for the "Select Company" dropdown on the user form. Slice 14 —
/// the new BE had no Company surface at all. visibleValue is "Code-Name" to
/// match the legacy label (e.g. "002-ICBC Company").
/// </summary>
public record ListCompaniesLovQuery : IRequest<IReadOnlyList<CompanyLovItemDto>>;

public class CompanyLovItemDto
{
    public int     companyId    { get; set; }
    public string? companyCode  { get; set; }
    public string? companyName  { get; set; }
    public string? visibleValue { get; set; }
}

public class ListCompaniesLovQueryHandler
    : IRequestHandler<ListCompaniesLovQuery, IReadOnlyList<CompanyLovItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListCompaniesLovQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<CompanyLovItemDto>> Handle(ListCompaniesLovQuery req, CancellationToken ct)
    {
        var rows = await _db.Companies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToListAsync(ct);

        return rows.Select(c => new CompanyLovItemDto
        {
            companyId    = c.Id,
            companyCode  = c.Code,
            companyName  = c.Name,
            visibleValue = string.IsNullOrWhiteSpace(c.Code) ? c.Name : $"{c.Code}-{c.Name}",
        }).ToList();
    }
}
