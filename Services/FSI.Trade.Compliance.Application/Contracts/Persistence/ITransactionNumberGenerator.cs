namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

/// <summary>
/// Generates a human-readable transaction number in the legacy format:
/// <c>{branchCode}{8-digit-zero-padded-sequence}</c> (e.g. <c>TLX00200001012</c>).
///
/// Backed by SQL Server sequence <c>dbo.TmX_Transaction_Sequence</c>. The
/// legacy backend wrapped this in <c>sp_GetNextTransactionSequenceNumber</c>;
/// the new backend calls the sequence directly, which retires that SP from
/// the cleanup-candidates list.
/// </summary>
public interface ITransactionNumberGenerator
{
    Task<string> NextAsync(string branchCode, CancellationToken ct = default);
}
