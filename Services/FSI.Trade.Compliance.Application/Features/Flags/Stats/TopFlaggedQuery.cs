using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Stats;

/// <summary>
/// "Which flags are most-flagged across transactions in a period."
/// On-the-fly aggregation per FSI direction (no materialised views).
///
/// Maps to <c>GET /api/v1/Flag/Stats/TopFlagged?from=...&amp;to=...&amp;productId=...&amp;take=10</c>.
///
/// Filters:
///   • From / To filter on TmX_Transaction_Flag.Set_Date (when the
///     flag's current state was last set). Excluding either side of
///     the range is allowed.
///   • ProductId filters by the underlying transaction's product.
///     Null → all products.
///   • Take is the result-row cap. Default 10.
/// </summary>
public record TopFlaggedQuery(
    DateTime?  From,
    DateTime?  To,
    int?       ProductId,
    int        Take = 10
) : IRequest<IReadOnlyList<TopFlaggedRowDto>>;

public class TopFlaggedRowDto
{
    public int     flagId                   { get; set; }
    public string  flagCode                 { get; set; } = "";
    public string  flagName                 { get; set; } = "";
    public string  flagDescription          { get; set; } = "";
    public int?    severityLkpId            { get; set; }
    public int?    flagCategoryLkpId        { get; set; }
    public decimal defaultWeight            { get; set; }
    public int     flaggedTransactionCount  { get; set; }
    /// <summary>Sum of (DefaultWeight × count) — naive risk-load score per flag.</summary>
    public decimal weightedScore            { get; set; }
}
