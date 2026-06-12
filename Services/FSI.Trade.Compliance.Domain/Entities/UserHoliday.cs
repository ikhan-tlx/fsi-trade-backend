namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// User out-of-office / holiday window over <c>TmX_User_Holiday</c>. Slice 14 —
/// added so the Users "User Holidays" tab
/// (GET /api/v1/User/GetHolidaysByUser/{id}) stops 404'ing.
/// </summary>
public class UserHoliday
{
    public int       UserHolidayId    { get; set; }
    public string    UserId           { get; set; } = "";
    public DateTime  HolidayStartDate { get; set; }
    public DateTime  HolidayEndDate   { get; set; }
    public bool?     IsProcessed      { get; set; }
    public bool?     IsActive         { get; set; }
    public string?   CreatedBy        { get; set; }
    public DateTime  CreatedDate      { get; set; }
}
