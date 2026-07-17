namespace FSI.Trade.Compliance.Application.Contracts.Kyc;

/// <summary>
/// Core sweep used by both the PeriodicReviewService (scheduled) and the admin
/// endpoints (run-now): find due customers, claim each schedule atomically,
/// delegate to <see cref="IKycReviewOrchestrator"/>, and record history.
/// </summary>
public interface IPeriodicReviewRunner
{
    /// <summary>Process up to <paramref name="batchSize"/> customers whose review is due. Returns the count processed.</summary>
    Task<int> RunDueAsync(int batchSize, int leadTimeDays, CancellationToken ct = default);

    /// <summary>Force one customer's review now (admin). Skips if no schedule or already in review.</summary>
    Task<ReviewRunOutcome> RunForCustomerAsync(string customerCode, CancellationToken ct = default);
}
