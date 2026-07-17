namespace FSI.Trade.Compliance.Application.Contracts.Customer;

/// <summary>
/// Upserts today's book-wide Customer Intelligence KPI snapshot row into
/// TmX_Customer_Insight_Daily. Idempotent — re-running on the same UTC day
/// refreshes the existing row rather than inserting a duplicate.
/// </summary>
public interface ICustomerInsightSnapshotWriter
{
    /// <returns><c>true</c> when today's row was inserted or refreshed.</returns>
    Task<bool> WriteTodayAsync(CancellationToken ct = default);
}
