using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Privileges.ListEntities;

public class ListPrivilegeEntitiesQueryHandler
    : IRequestHandler<ListPrivilegeEntitiesQuery, List<PrivilegeEntityDto>>
{
    private readonly IApplicationDbContext _db;
    public ListPrivilegeEntitiesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PrivilegeEntityDto>> Handle(ListPrivilegeEntitiesQuery req, CancellationToken ct)
    {
        // NOTE — Active_Flag intentionally NOT filtered (see PrivilegeService.cs / BACKLOG).
        var rows = await _db.Privileges
            .AsNoTracking()
            .Where(p => p.Name != null)
            .Select(p => new PrivilegeRowDto
            {
                privilegeId = p.Id,
                code        = p.Name!,
                description = p.Description
            })
            .OrderBy(p => p.code)
            .ToListAsync(ct);

        return rows
            .GroupBy(p => Split(p.code).Entity, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PrivilegeEntityDto
            {
                entity     = g.Key,
                privileges = g.OrderBy(x => x.code, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .ToList();
    }

    /// <summary>
    /// "Users.Create" → ("Users", "Create"). "View Entity" or "Create" with no
    /// dot → ("Common", "View Entity"). Trims whitespace; never throws.
    /// </summary>
    private static (string Entity, string Action) Split(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (PrivilegeEntityDto.UngroupedEntityName, "");

        var dot = code.IndexOf('.');
        if (dot <= 0 || dot == code.Length - 1)
            return (PrivilegeEntityDto.UngroupedEntityName, code.Trim());

        return (code[..dot].Trim(), code[(dot + 1)..].Trim());
    }
}
