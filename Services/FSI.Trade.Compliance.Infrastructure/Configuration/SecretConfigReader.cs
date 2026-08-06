using FSI.Trade.Compliance.Application.Contracts.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>
/// Reads a sensitive configuration value directly from
/// <c>TmX_Configuration_Definition/_Value</c> (the Global, active row) and returns
/// it decrypted via <see cref="ISecretProtector"/>. Used for secrets that are NOT
/// published through the DB→IConfiguration bridge. The plaintext is only held in
/// the returned string; it is never logged. On any DB/decrypt error the reader
/// logs (without the value) and returns null so the caller can fail safe.
/// </summary>
public sealed class SecretConfigReader : ISecretConfigReader
{
    private readonly string _connectionString;
    private readonly ISecretProtector _protector;
    private readonly ILogger<SecretConfigReader> _log;

    public SecretConfigReader(string connectionString, ISecretProtector protector, ILogger<SecretConfigReader> log)
    {
        _connectionString = connectionString;
        _protector = protector;
        _log = log;
    }

    public string? Get(string configurationKey)
    {
        var raw = ReadRaw(configurationKey);
        if (string.IsNullOrEmpty(raw)) return null;
        var plain = _protector.Unprotect(raw);
        return string.IsNullOrEmpty(plain) ? null : plain;
    }

    public bool HasValue(string configurationKey) => !string.IsNullOrEmpty(ReadRaw(configurationKey));

    private string? ReadRaw(string configurationKey)
    {
        const string sql = @"
SELECT COALESCE(v.Configuration_Value, d.Default_Value)
FROM   dbo.TmX_Configuration_Definition d
LEFT JOIN dbo.TmX_Configuration_Value v
       ON v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  d.Configuration_Key = @key AND d.Is_Active = 1;";
        try
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@key", configurationKey);
            var o = cmd.ExecuteScalar();
            return o is null || o is DBNull ? null : (string?)o;
        }
        catch (Exception ex)
        {
            // Never include the value in the log.
            _log.LogError(ex, "SecretConfigReader failed to read configuration key {Key}.", configurationKey);
            return null;
        }
    }
}
