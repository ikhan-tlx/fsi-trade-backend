using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;   // RiskBand / RiskBandClassifier

namespace FSI.Trade.Compliance.Application.Features.Customer.Common;

/// <summary>
/// Product trade direction, derived from the product name/code because
/// <c>TmX_Product</c> has no explicit direction column. Tunable keyword match.
/// Promotable to a real <c>Product.Direction</c> column later without touching
/// callers (see CUSTOMER_INSIGHT_BACKEND_CHANGE_PLAN §6 opt).
/// </summary>
public enum TradeDirection { Import, Export, Unclassified }

public static class ProductDirectionClassifier
{
    public static TradeDirection Classify(string? productName, string? productCode = null)
    {
        var s = ((productName ?? "") + " " + (productCode ?? "")).ToLowerInvariant();
        if (s.Contains("import")) return TradeDirection.Import;
        if (s.Contains("export")) return TradeDirection.Export;
        // Common trade-finance leanings when the word isn't explicit.
        if (s.Contains("inward") || s.Contains("shipping guarantee")) return TradeDirection.Import;
        if (s.Contains("outward")) return TradeDirection.Export;
        return TradeDirection.Unclassified;
    }
}

/// <summary>
/// Composite 0–99 risk score. The stored risk is only a 4-level band
/// (PEP/High/Medium/Low), so the design's granular number is DERIVED here:
/// a band base plus a bump for the weight of the customer's active flags,
/// clamped. Returns null when there is no signal at all (Unknown band and no
/// flags) so the UI can show "not assessed" rather than a misleading 0.
/// This is an explicit, tunable product metric — documented as derived.
/// </summary>
public static class RiskScoreComposite
{
    public static int? Compute(RiskBand band, double activeFlagWeightSum)
    {
        int? baseScore = band switch
        {
            RiskBand.Pep    => 92,
            RiskBand.High   => 78,
            RiskBand.Medium => 52,
            RiskBand.Low    => 26,
            _               => null   // Unknown
        };

        if (baseScore is null)
        {
            if (activeFlagWeightSum <= 0) return null;          // truly no signal
            baseScore = 45;                                     // flags but no band → "elevated, unrated"
        }

        // Each unit of flag weight nudges the score up; capped so a Low customer
        // with flags can't outrank a genuine High/PEP.
        var bump = (int)Math.Round(Math.Min(activeFlagWeightSum * 4.0, 18.0));
        return Math.Clamp(baseScore.Value + bump, 0, 99);
    }
}

/// <summary>Composite triage health for the listing/cards.</summary>
public enum CustomerHealth { Red, Amber, Green, Grey }

public static class CustomerHealthEvaluator
{
    /// <summary>
    /// Red   = any open case OR any critical flag OR review overdue.
    /// Amber = high-risk band OR review due-soon OR ≥1 active high-severity flag.
    /// Green = has data and none of the above.
    /// Grey  = no signal at all (no txns/flags/reviews/risk).
    /// </summary>
    public static CustomerHealth Evaluate(
        int openCases, int criticalFlags, bool reviewOverdue,
        bool highRisk, bool reviewDueSoon, int activeHighFlags, bool hasAnySignal)
    {
        if (openCases > 0 || criticalFlags > 0 || reviewOverdue) return CustomerHealth.Red;
        if (highRisk || reviewDueSoon || activeHighFlags > 0)    return CustomerHealth.Amber;
        return hasAnySignal ? CustomerHealth.Green : CustomerHealth.Grey;
    }

    public static string Label(CustomerHealth h) => h switch
    {
        CustomerHealth.Red   => "red",
        CustomerHealth.Amber => "amber",
        CustomerHealth.Green => "green",
        _                    => "grey"
    };
}

/// <summary>Review urgency derived from the schedule's next-review date.</summary>
public enum ReviewState { Overdue, DueSoon, Ok, None }

public static class ReviewStateEvaluator
{
    /// <summary>Overdue if next review is in the past; DueSoon within the window (default 14d).</summary>
    public static ReviewState Evaluate(DateTime? nextReviewUtc, DateTime nowUtc, int dueSoonDays = 14)
    {
        if (nextReviewUtc is null) return ReviewState.None;
        var days = (nextReviewUtc.Value.Date - nowUtc.Date).TotalDays;
        if (days < 0)             return ReviewState.Overdue;
        if (days <= dueSoonDays)  return ReviewState.DueSoon;
        return ReviewState.Ok;
    }

    public static string Label(ReviewState s) => s switch
    {
        ReviewState.Overdue => "Overdue",
        ReviewState.DueSoon => "DueSoon",
        ReviewState.Ok      => "OK",
        _                   => "None"
    };
}
