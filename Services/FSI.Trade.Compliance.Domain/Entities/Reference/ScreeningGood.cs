namespace FSI.Trade.Compliance.Domain.Entities.Reference;

/// <summary>
/// Read-only row of the high-risk goods / HS-code watchlist. Maps to the
/// existing <c>dbo.REPO_SCREENING_GOODS</c> table. The engine screens a
/// transaction's HS-code fields against this for the goods auto flags.
/// Keyless (the table has no primary key) — query-only.
/// </summary>
public class ScreeningGood
{
    public string GoodCode        { get; set; } = "";
    public string GoodName        { get; set; } = "";
    public string GoodDescription { get; set; } = "";
}
