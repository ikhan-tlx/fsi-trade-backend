using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

// =====================================================================
// The IOptions bridge (Configuration Management, Phase 1 — design §5.4).
//
// Publishes DB-managed settings (TmX_Configuration_Definition + active
// Global TmX_Configuration_Value) into the .NET IConfiguration key space,
// LAYERED AFTER appsettings.json, so DB values OVERRIDE file values for the
// same key. Because a setting's Configuration_Key is the dotted appsettings
// path, every existing IOptions/IOptionsMonitor<T> consumer
// (PeriodicReviewOptions, PasswordPolicy, TradeCaseDefaults, …) transparently
// reads the DB value with ZERO call-site change. On a config write, the admin
// handlers call IConfigurationRefresher.Refresh() → ReloadAll() → each
// provider reloads and raises its reload token → IOptionsMonitor re-binds
// (no restart).
//
// EXCLUDED from the bridge (Phase 1):
//   • Is_Sensitive = 1  → secrets stay in appsettings/secret store (§8).
//   • Category 'logging' / Data_Type 'LogModule' → Serilog level-switch
//     hot-reload is Phase 2 (see the backlog); stored+editable but not bridged.
// JSON values are FLATTENED into child keys so dictionary/POCO options bind
// (e.g. Integration.FccmActionCodeStatusMap → Integration:FccmActionCodeStatusMap:30004).
//
// Resilience: Load() never throws — if the DB/table is unavailable (e.g. a
// fresh database before the migration runs) the app still boots on appsettings.
// =====================================================================

public sealed class DbConfigurationSource : IConfigurationSource
{
    private readonly string _connectionString;
    public DbConfigurationSource(string connectionString) => _connectionString = connectionString;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new DbConfigurationProvider(_connectionString);
}

public sealed class DbConfigurationProvider : ConfigurationProvider
{
    private readonly string _connectionString;

    // All live providers, so a config write can refresh every one.
    private static readonly ConcurrentBag<DbConfigurationProvider> Instances = new();

    public DbConfigurationProvider(string connectionString)
    {
        _connectionString = connectionString;
        Instances.Add(this);
    }

    /// <summary>Reload every provider and raise reload tokens → IOptionsMonitor re-binds.</summary>
    public static void ReloadAll()
    {
        foreach (var p in Instances)
        {
            p.Load();
            p.OnReload();
        }
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            const string sql = @"
SELECT d.Configuration_Key, d.Data_Type,
       COALESCE(v.Configuration_Value, d.Default_Value) AS EffectiveValue
FROM   dbo.TmX_Configuration_Definition d
LEFT JOIN dbo.TmX_Configuration_Value v
       ON v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  d.Is_Active = 1
  AND  d.Is_Sensitive = 0
  AND  d.Data_Type <> 'LogModule'
  AND  d.Category_Key <> 'logging';";
            using var cmd = new SqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                var type = r.GetString(1);
                var val = r.IsDBNull(2) ? null : r.GetString(2);
                if (val is null) continue;

                var colonKey = key.Replace('.', ':');
                if (string.Equals(type, "Json", StringComparison.OrdinalIgnoreCase))
                    FlattenJson(colonKey, val, data);
                else
                    data[colonKey] = val;
            }
        }
        catch
        {
            // Swallow: fall back to appsettings-only if DB/table is unavailable.
            // (Fresh DB before migration, or transient connectivity at boot.)
        }
        Data = data;
    }

    private static void FlattenJson(string prefix, string json, IDictionary<string, string?> data)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(prefix, doc.RootElement, data);
        }
        catch
        {
            // Malformed JSON → publish the raw string so at least binding-as-string works.
            data[prefix] = json;
        }
    }

    private static void Walk(string prefix, JsonElement el, IDictionary<string, string?> data)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    Walk($"{prefix}:{prop.Name}", prop.Value, data);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in el.EnumerateArray())
                    Walk($"{prefix}:{i++}", item, data);
                break;
            case JsonValueKind.String:
                data[prefix] = el.GetString();
                break;
            case JsonValueKind.Number:
                data[prefix] = el.GetRawText();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                data[prefix] = el.GetBoolean() ? "true" : "false";
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                break;
        }
    }
}

public static class DbConfigurationBuilderExtensions
{
    /// <summary>Layer DB-managed configuration on top of the existing sources.</summary>
    public static IConfigurationBuilder AddDbConfiguration(this IConfigurationBuilder builder, string connectionString)
        => builder.Add(new DbConfigurationSource(connectionString));
}
