using System.Data;
using System.Data.Common;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

/// <summary>
/// ADO.NET implementation of <see cref="IStoredProcedureRunner"/>. Runs
/// the requested stored procedure with named parameters and materialises
/// the first result-set into a <see cref="DataTable"/>.
///
/// Borrows the same connection-management pattern as
/// <see cref="TransactionNumberGenerator"/> — we grab the
/// <see cref="DbConnection"/> off EF's context so the SP runs against the
/// same connection string the rest of the app uses, and we leave the
/// connection in whatever state we found it in.
///
/// Param-name normalisation: callers may pass <c>"@CustomerId"</c> or
/// <c>"CustomerId"</c>; we always add the '@' prefix on the way to ADO.
/// NULL values are translated to <see cref="DBNull.Value"/>.
/// </summary>
internal class StoredProcedureRunner : IStoredProcedureRunner
{
    private readonly ApplicationDbContext _db;

    public StoredProcedureRunner(ApplicationDbContext db) => _db = db;

    public async Task<DataTable> ExecuteAsync(
        string storedProcedureName,
        IReadOnlyDictionary<string, object?> parameters,
        int? commandTimeoutSeconds = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storedProcedureName))
            throw new ArgumentException("Stored procedure name is required.", nameof(storedProcedureName));

        var connection = _db.Database.GetDbConnection();
        var wasClosed  = connection.State == ConnectionState.Closed;

        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = storedProcedureName;
            cmd.CommandType = CommandType.StoredProcedure;
            if (commandTimeoutSeconds is > 0) cmd.CommandTimeout = commandTimeoutSeconds.Value;

            foreach (var kv in parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = kv.Key.StartsWith('@') ? kv.Key : "@" + kv.Key;
                p.Value         = CoerceParameterValue(kv.Value);
                cmd.Parameters.Add(p);
            }

            var table = new DataTable();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                table.Load(reader);
            }
            return table;
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Coerces the parameter value into something SqlClient can bind. The
    /// usual offender is System.Text.Json's <see cref="JsonElement"/> —
    /// when the controller binds <c>Dictionary&lt;string, object&gt;</c>
    /// from a JSON body, every value comes through as a JsonElement
    /// regardless of its underlying primitive kind. SqlClient can't bind
    /// JsonElement directly, so we unwrap it here.
    /// </summary>
    private static object CoerceParameterValue(object? raw)
    {
        if (raw is null) return DBNull.Value;

        if (raw is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Null      => DBNull.Value,
                JsonValueKind.Undefined => DBNull.Value,
                JsonValueKind.String    => el.GetString() is string s ? s : (object)DBNull.Value,
                JsonValueKind.True      => true,
                JsonValueKind.False     => false,
                JsonValueKind.Number    => el.TryGetInt64(out var i)
                                              ? i
                                              : (el.TryGetDecimal(out var m)
                                                    ? m
                                                    : (object)el.GetDouble()),
                // Arrays / objects fall through to their JSON text — useful
                // for SPs that accept a JSON parameter, harmless otherwise
                // (SP that didn't expect a JSON blob will just fail with a
                // clear error).
                _                       => el.GetRawText()
            };
        }

        return raw;
    }
}
