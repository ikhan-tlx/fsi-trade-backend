using FSI.Trade.Compliance.Application.Features.Transactions.Create;

namespace FSI.Trade.Compliance.Application.Contracts.Kyc;

/// <summary>
/// Clones a source KYC transaction for a periodic review: builds the create
/// command (header + customer) and copies the product payload (detail UDF +
/// grids + customer snapshot udf) — excluding workflow/audit state.
/// </summary>
public interface IKycTransactionCloner
{
    /// <summary>Header + basic customer, mapped from the source terminal KYC.</summary>
    Task<CreateTransactionCommand> BuildCreateCommandAsync(int sourceTransactionId, CancellationToken ct = default);

    /// <summary>Copies detail UDF + beneficiary/stakeholder grids + the customer snapshot udf_data.</summary>
    Task CopyProductDataAsync(int sourceTransactionId, int newTransactionId, CancellationToken ct = default);
}
