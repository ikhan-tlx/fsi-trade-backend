namespace FSI.Trade.Compliance.Application.Contracts.Integrations;

/// <summary>
/// Reads a customer's most-recent KYC screening result (risk score + name).
/// Application contract — vendor-agnostic. Today's only implementation
/// (<c>BrainsKycScreeningService</c>) calls BRAINS over HTTP; tomorrow's
/// could be a different vendor with no consumer-side change.
/// </summary>
public interface IKycScreeningService
{
    /// <summary>Returns the latest screening result for the given customer, or null if BRAINS has no record.</summary>
    Task<KycResult?> GetKycForCustomerAsync(string customerId, CancellationToken ct = default);
}

public class KycResult
{
    public string  RiskScore     { get; set; } = "";    // "Low" / "Medium" / "High" / etc.
    public string? CustomerName  { get; set; }
}
