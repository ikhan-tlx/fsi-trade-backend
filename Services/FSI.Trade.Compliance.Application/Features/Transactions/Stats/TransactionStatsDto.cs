namespace FSI.Trade.Compliance.Application.Features.Transactions.Stats;

/// <summary>
/// KPI summary for the Trade Repository listing (Option A redesign).
/// Every count is scoped to the same caller predicate the grid uses
/// (location-tree OR effective branches OR own-created), so the numbers
/// always match what the user can actually see in the list. The FE turns
/// these into the clickable KPI cards + the per-product segment control.
/// </summary>
public class TransactionStatsDto
{
    /// <summary>All transactions visible to the caller.</summary>
    public int total            { get; set; }

    /// <summary>Currently sitting in the caller's own inbox (Inbox_User_ID == me).</summary>
    public int awaitingMyAction { get; set; }

    /// <summary>In the "Sent To Authorizer" state.</summary>
    public int sentToAuthorizer { get; set; }

    /// <summary>Created today (server date).</summary>
    public int createdToday     { get; set; }

    /// <summary>Have at least one currently-raised flag.</summary>
    public int flagged          { get; set; }

    /// <summary>
    /// Aging SLA (days) echoed from <c>TradeRepository:SlaDays</c> so the FE
    /// can colour the per-row aging badge without its own config copy.
    /// </summary>
    public int slaDays          { get; set; }

    /// <summary>Per-product counts for the segment control (All / Import / Export / …).</summary>
    public List<ProductStatDto> byProduct { get; set; } = new();
}

/// <summary>One product bucket inside <see cref="TransactionStatsDto.byProduct"/>.</summary>
public class ProductStatDto
{
    public int    productId   { get; set; }
    public string productName { get; set; } = "";
    public int    count       { get; set; }
}
