namespace FSI.Trade.Compliance.Application.Features.Dashboard.Summary;

/// <summary>
/// Resolves the dashboard <c>range</c> token to a UTC window. Default 30d.
/// </summary>
public static class DashboardRange
{
    public static (string Normalized, DateTime From, DateTime To) Resolve(string? range, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var token = (range ?? "").Trim();

        switch (token.ToUpperInvariant())
        {
            case "7D":
                return ("7d", now.AddDays(-7), now);
            case "QTD":
                var qStartMonth = ((now.Month - 1) / 3) * 3 + 1;          // 1,4,7,10
                return ("QTD", new DateTime(now.Year, qStartMonth, 1, 0, 0, 0, DateTimeKind.Utc), now);
            case "YTD":
                return ("YTD", new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), now);
            case "30D":
            default:
                return ("30d", now.AddDays(-30), now);                    // default
        }
    }
}
