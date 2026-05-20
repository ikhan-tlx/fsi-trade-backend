using System.Data;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITransactionNumberGenerator"/>.
/// Pulls the next value directly from <c>dbo.TmX_Transaction_Sequence</c> —
/// same source the legacy <c>sp_GetNextTransactionSequenceNumber</c> wrapped,
/// just one fewer layer of indirection.
///
/// IMPLEMENTATION NOTE — why ADO.NET instead of <c>SqlQueryRaw</c>:
/// EF Core 8's <c>SqlQueryRaw&lt;T&gt;.FirstAsync()</c> wraps the user SQL
/// into a subquery (<c>SELECT TOP(1) [Value] FROM (...) AS x</c>) for
/// execution. SQL Server forbids <c>NEXT VALUE FOR</c> inside subqueries,
/// CTEs, views, derived tables, and a bunch of other contexts. So we
/// bypass EF's translation and run the statement as a plain
/// <c>ExecuteScalarAsync</c> on the underlying connection.
///
/// Pads the sequence value to <see cref="SequenceLength"/> digits (legacy
/// constant <c>TRADE_SEQUENCE_LENGTH = 8</c>) with leading zeros, prefixes
/// with the branch code → e.g. <c>"TLX" + "00000123" = "TLX00000123"</c>.
/// </summary>
internal class TransactionNumberGenerator : ITransactionNumberGenerator
{
    private const int SequenceLength = 8;

    private readonly ApplicationDbContext _db;

    public TransactionNumberGenerator(ApplicationDbContext db) => _db = db;

    public async Task<string> NextAsync(string branchCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code is required for transaction number generation.", nameof(branchCode));

        var connection = _db.Database.GetDbConnection();
        var wasClosed  = connection.State == ConnectionState.Closed;

        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT NEXT VALUE FOR dbo.TmX_Transaction_Sequence";
            cmd.CommandType = CommandType.Text;

            var raw = await cmd.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("TmX_Transaction_Sequence returned NULL.");

            var next = Convert.ToInt64(raw);
            return branchCode + next.ToString().PadLeft(SequenceLength, '0');
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
