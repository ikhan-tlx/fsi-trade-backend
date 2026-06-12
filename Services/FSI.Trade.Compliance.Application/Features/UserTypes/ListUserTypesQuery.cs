using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.UserTypes;

/// <summary>
/// User-type reference list for the Users screen (the "User Type" dropdown
/// bound to <c>ApplicationUser.UserTypeLkpId</c> → <c>User_Type_Lkp_ID</c>).
///
/// In the legacy app this was NOT a dedicated table — user types are rows in
/// <c>TmX_Lookup</c> with <c>Lookup_Type = 'USER'</c>, served via
/// <c>GET /api/v1/Lookup/GetByLookupType/USER</c> and consumed by
/// <c>lookupService.getLocalLookupsByType(lkupType.USER)</c> in
/// users.service.js. We surface the same data under the dedicated
/// <c>GET /api/v1/UserTypes</c> route the new FE already calls.
/// </summary>
public record ListUserTypesQuery : IRequest<IReadOnlyList<UserTypeLookupDto>>;

public class UserTypeLookupDto
{
    public int     lookupId     { get; set; }   // Lookup_ID
    public string? hiddenValue  { get; set; }   // Hidden_Value (code stored on the user row)
    public string? visibleValue { get; set; }   // Visible_Value (display label)
    public int?    sortOrder    { get; set; }
}

public class ListUserTypesQueryHandler
    : IRequestHandler<ListUserTypesQuery, IReadOnlyList<UserTypeLookupDto>>
{
    // Legacy lookup-type key for user types (tmx-finance-constants.js: USER:'USER').
    private const string UserTypeLookupType = "USER";

    private readonly IApplicationDbContext _db;
    public ListUserTypesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserTypeLookupDto>> Handle(ListUserTypesQuery req, CancellationToken ct)
    {
        return await _db.Lookups
            .AsNoTracking()
            .Where(l => l.IsActive && l.LookupType == UserTypeLookupType)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.VisibleValue)
            .Select(l => new UserTypeLookupDto
            {
                lookupId     = l.Id,
                hiddenValue  = l.HiddenValue,
                visibleValue = l.VisibleValue,
                sortOrder    = l.SortOrder
            })
            .ToListAsync(ct);
    }
}
