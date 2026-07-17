namespace FSI.Trade.Compliance.Application.Contracts.Kyc;

/// <summary>
/// Runs a single periodic-review cycle for a customer: clone the last terminal
/// KYC, create the new transaction, copy product data, and drive the workflow to
/// finalisation as the PERIODIC REVIEWER principal (which fires onboarding via
/// the existing workflow finalise hook). Reuses the create-gate as the
/// "KYC already in progress" guard.
/// </summary>
public interface IKycReviewOrchestrator
{
    Task<ReviewRunOutcome> RunAsync(string customerCode, int sourceTransactionId, CancellationToken ct = default);
}

public record ReviewRunOutcome(string Status, int? NewTransactionId, long? KycRequestId, string? Remark);

public static class ReviewOutcome
{
    public const string Completed = "Completed";   // new KYC created + workflow finalised + onboarding fired
    public const string Blocked   = "Blocked";     // create-gate: a KYC is already in progress
    public const string Failed    = "Failed";      // clone/create/workflow error
    public const string Skipped   = "Skipped";     // nothing to do (no source / no customer)
}
