using System.Text.Json.Serialization;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.Holidays;

/// <summary>
/// A user's holiday windows over <c>TmX_User_Holiday</c>. Slice 14. The FE
/// Holidays tab reads PascalCase keys, so the DTO pins them with
/// [JsonPropertyName] (the API otherwise serialises camelCase).
/// </summary>
public record GetUserHolidaysQuery(string UserId) : IRequest<IReadOnlyList<UserHolidayDto>>;

public class UserHolidayDto
{
    [JsonPropertyName("UserHolidayID")]   public int     UserHolidayID    { get; set; }
    [JsonPropertyName("HolidayStartDate")] public string? HolidayStartDate { get; set; }
    [JsonPropertyName("HolidayEndDate")]   public string? HolidayEndDate   { get; set; }
    [JsonPropertyName("IsActive")]         public int     IsActive         { get; set; }
    [JsonPropertyName("IsProcessed")]      public int     IsProcessed      { get; set; }
}

public class GetUserHolidaysQueryHandler
    : IRequestHandler<GetUserHolidaysQuery, IReadOnlyList<UserHolidayDto>>
{
    private readonly IApplicationDbContext _db;
    public GetUserHolidaysQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserHolidayDto>> Handle(GetUserHolidaysQuery req, CancellationToken ct)
    {
        var id = (req.UserId ?? "").Trim();
        if (id.Length == 0) return new List<UserHolidayDto>();

        var rows = await _db.UserHolidays
            .AsNoTracking()
            .Where(h => h.UserId == id)
            .OrderByDescending(h => h.HolidayStartDate)
            .Select(h => new
            {
                h.UserHolidayId,
                h.HolidayStartDate,
                h.HolidayEndDate,
                h.IsActive,
                h.IsProcessed
            })
            .ToListAsync(ct);

        return rows.Select(h => new UserHolidayDto
        {
            UserHolidayID    = h.UserHolidayId,
            HolidayStartDate = h.HolidayStartDate.ToString("yyyy-MM-dd"),
            HolidayEndDate   = h.HolidayEndDate.ToString("yyyy-MM-dd"),
            IsActive         = (h.IsActive ?? false) ? 1 : 0,
            IsProcessed      = (h.IsProcessed ?? false) ? 1 : 0
        }).ToList();
    }
}
