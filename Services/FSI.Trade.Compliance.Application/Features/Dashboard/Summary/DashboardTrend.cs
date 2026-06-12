namespace FSI.Trade.Compliance.Application.Features.Dashboard.Summary;

/// <summary>
/// Phase 1.5 — builds a COUNT trend (new instruments per time bucket) over the
/// range, as a stand-in for the Phase-2 USD exposure trend (no money/FX data
/// yet). Granularity: 7d → daily, 30d → weekly, QTD/YTD → monthly.
/// <c>unit = "count"</c> so the FE labels the axis correctly.
/// </summary>
public static class DashboardTrend
{
    public static ExposureTrendDto Build(string range, DateTime from, DateTime to, IReadOnlyList<DateTime> createdUtc)
    {
        var buckets = MakeBuckets(range, from, to);
        var counts  = new int[buckets.Count];

        foreach (var d in createdUtc)
        {
            // Assign to the last bucket whose start is <= the date.
            var idx = -1;
            for (var i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Start <= d) idx = i;
                else break;
            }
            if (idx >= 0) counts[idx]++;
        }

        return new ExposureTrendDto
        {
            unit   = "count",
            points = buckets.Select((b, i) => new TrendPointDto { label = b.Label, value = counts[i] }).ToList()
        };
    }

    private static List<(DateTime Start, string Label)> MakeBuckets(string range, DateTime from, DateTime to)
    {
        var list = new List<(DateTime, string)>();

        switch (range.ToUpperInvariant())
        {
            case "7D":   // daily
                for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
                    list.Add((d, d.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)));
                break;

            case "QTD":
            case "YTD":  // monthly
                var m = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                while (m <= to)
                {
                    list.Add((m, m.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture)));
                    m = m.AddMonths(1);
                }
                break;

            default:     // "30d" → weekly
                var start = from;
                var wk = 1;
                while (start < to)
                {
                    list.Add((start, "Wk " + wk));
                    start = start.AddDays(7);
                    wk++;
                }
                break;
        }

        if (list.Count == 0) list.Add((from, "—"));   // degenerate range guard
        return list;
    }
}
