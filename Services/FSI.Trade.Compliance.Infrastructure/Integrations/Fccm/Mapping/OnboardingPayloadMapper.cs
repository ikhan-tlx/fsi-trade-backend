using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Customer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm.Mapping;

/// <summary>
/// Per-submission inputs the mapper can read from: the customer snapshot row, the
/// two UDF JSON blobs (transaction detail + customer), and runtime context values
/// (actor, applicationId, requestId, transactionId, customerId).
/// </summary>
public sealed class OnboardingMappingContext
{
    public TransactionCustomerSnapshot? Snapshot { get; init; }
    public string? DetailUdf   { get; init; }
    public string? CustomerUdf { get; init; }
    public IReadOnlyDictionary<string, string?> Context { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Generic, declarative onboarding-payload builder. Walks a <see cref="MappingProfile"/>
/// against a <see cref="OnboardingMappingContext"/> and produces a
/// <see cref="JsonObject"/> ready to POST to FCCM. No field names are hardcoded here —
/// the shape lives entirely in the profile. Reuses the same KYC-derivation primitives
/// as the legacy pipeline (raw UDF values, Lookup_ID → Hidden_Value, and the Oracle
/// FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score lookup) exposed as named "source kinds".
/// </summary>
public sealed class OnboardingPayloadMapper
{
    private readonly IApplicationDbContext _db;
    private readonly FccmOracleReader      _oracle;
    private readonly ILogger<OnboardingPayloadMapper> _log;

    public OnboardingPayloadMapper(
        IApplicationDbContext db,
        FccmOracleReader      oracle,
        ILogger<OnboardingPayloadMapper> log)
    {
        _db     = db;
        _oracle = oracle;
        _log    = log;
    }

    public async Task<JsonObject> MapAsync(MappingProfile profile, OnboardingMappingContext ctx, CancellationToken ct = default)
    {
        using var detailDoc = TryParse(ctx.DetailUdf);
        using var custDoc   = TryParse(ctx.CustomerUdf);

        var run = new MapRun(profile, ctx,
            detailDoc?.RootElement, custDoc?.RootElement,
            new Dictionary<int, string?>(),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        var root = new JsonObject();

        foreach (var field in profile.Fields)
            await ApplyFieldAsync(root, field, run, row: null, ct);

        foreach (var arr in profile.Arrays)
        {
            var built = await BuildArrayAsync(arr, run, parentRow: null, ct);
            if (built is not null) SetPath(root, arr.Target, built, alwaysSet: true, run);
        }

        _log.LogDebug("Onboarding payload built from profile '{Profile}' ({Fields} fields, {Arrays} arrays).",
            profile.Metadata.Name, profile.Fields.Count, profile.Arrays.Count);
        return root;
    }

    // ---- Field / array walkers -------------------------------------------------

    private async Task ApplyFieldAsync(JsonObject scope, FieldRule field, MapRun run, RowScope? row, CancellationToken ct)
    {
        var raw = await ResolveAsync(field.From, run, row, ct);
        var (val, castTo) = ApplyTransforms(raw, field.Transform, run);
        var leaf = BuildLeaf(val, castTo);

        var emitNulls = run.Profile.Settings.EmitNulls;
        var omit = field.OmitWhenNull ?? !emitNulls;
        if (leaf is null && omit) return;

        SetPath(scope, field.Target, leaf, alwaysSet: false, run);
    }

    private async Task<JsonArray?> BuildArrayAsync(ArrayRule rule, MapRun run, RowScope? parentRow, CancellationToken ct)
    {
        var array = new JsonArray();

        if (string.Equals(rule.Mode, "perRow", StringComparison.OrdinalIgnoreCase))
        {
            var rows = EnumerateRows(rule.Rows, run);
            foreach (var rs in rows)
                array.Add(await BuildElementAsync(rule.Element, run, rs, ct));
        }
        else // "single"
        {
            array.Add(await BuildElementAsync(rule.Element, run, parentRow, ct));
        }

        if (array.Count == 0 && rule.OmitWhenEmpty) return null;
        return array;
    }

    private async Task<JsonObject> BuildElementAsync(ElementSpec element, MapRun run, RowScope? row, CancellationToken ct)
    {
        var obj = new JsonObject();
        foreach (var field in element.Fields)
            await ApplyFieldAsync(obj, field, run, row, ct);
        foreach (var arr in element.Arrays)
        {
            var built = await BuildArrayAsync(arr, run, parentRow: row, ct);
            if (built is not null) SetPath(obj, arr.Target, built, alwaysSet: true, run);
        }
        return obj;
    }

    // ---- Source resolution -----------------------------------------------------

    private async Task<string?> ResolveAsync(SourceSpec src, MapRun run, RowScope? row, CancellationToken ct)
    {
        switch ((src.Kind ?? "").ToLowerInvariant())
        {
            case "const":
                if (!string.IsNullOrEmpty(src.Ref) && run.Profile.Constants.TryGetValue(src.Ref!, out var c))
                    return ElemToString(c);
                return src.Value.HasValue ? ElemToString(src.Value.Value) : null;

            case "context":
                return src.Name is not null && run.Ctx.Context.TryGetValue(src.Name, out var cv) ? cv : null;

            case "snapshot":
                return SnapshotValue(run.Ctx.Snapshot, src.Name);

            case "udf.direct":
                return Raw(run.Detail, src.Key);

            case "udf.hidden":
                return TryGetId(run.Detail, src.Key) is int hid ? await HiddenAsync(hid, run, ct) : null;

            case "udf.auto":
                // Config-driven: consult the field's TmX_Tenant_Field_Setup.Field_Lookup_Type.
                // If set, the stored value is a TmX_Lookup Id -> emit its Hidden_Value
                // (like a dropdown/country field). If not set, the field carries a literal.
                return await ResolveUdfAutoAsync(run, src.Key, ct);

            case "grid.maxscore":
                return await GridMaxScoreAsync(run, src.Grid, src.Param, ct);

            case "grid.first":
                return await GridFirstAsync(run, src.Grid, src.Param, ct);

            case "row.value":
                if (row is null) return null;
                if (src.RowKey is not null) return Raw(row.Row, src.RowKey);
                if (row.Hydrated)   // perRow hydrate=hidden → resolve the row's Lookup_ID to its hidden value
                    return TryGetId(row.Row, row.Param) is int rvid ? await HiddenAsync(rvid, run, ct) : null;
                return row.Primary;

            case "row.hidden":
                if (row is null) return null;
                var key = src.RowKey ?? src.Param;
                return TryGetId(row.Row, key) is int rid ? await HiddenAsync(rid, run, ct) : null;

            default:
                _log.LogWarning("Onboarding mapping: unknown source kind '{Kind}' — resolving to null.", src.Kind);
                return null;
        }
    }

    private IEnumerable<RowScope> EnumerateRows(RowSource? rows, MapRun run)
    {
        if (rows is null || string.IsNullOrWhiteSpace(rows.Grid)) yield break;
        if (run.Cust is not { ValueKind: JsonValueKind.Object }) yield break;
        if (!run.Cust.Value.TryGetProperty(rows.Grid, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;

        var hidden = string.Equals(rows.Hydrate, "hidden", StringComparison.OrdinalIgnoreCase);
        foreach (var rowEl in arr.EnumerateArray())
        {
            if (rowEl.ValueKind != JsonValueKind.Object) continue;
            string? primary;
            if (hidden)
            {
                // Resolve synchronously via the per-run cache is not possible here (async);
                // primary is left null and row.hidden should be used, OR raw param value.
                primary = TryGetIdRaw(rowEl, rows.Param);   // Lookup_ID as string fallback
            }
            else
            {
                primary = Raw(rowEl, rows.Param);
            }
            yield return new RowScope(rowEl, primary, hidden, rows.Param);
        }
    }

    // ---- KYC-derivation primitives (mirror KycOnboardingFieldResolver) ----------

    private async Task<string?> GridMaxScoreAsync(MapRun run, string? grid, string? param, CancellationToken ct)
    {
        var codes = new List<string>();
        foreach (var id in GridIds(run.Cust, grid, param))
        {
            var h = await HiddenAsync(id, run, ct);
            if (!string.IsNullOrWhiteSpace(h)) codes.Add(h!);
        }
        if (codes.Count == 0) return null;
        return await _oracle.GetMaxScoreCodeValueAsync(codes, ct);
    }

    private async Task<string?> GridFirstAsync(MapRun run, string? grid, string? param, CancellationToken ct)
    {
        var ids = GridIds(run.Cust, grid, param);
        return ids.Count > 0 ? await HiddenAsync(ids[0], run, ct) : null;
    }

    /// <summary>
    /// Resolves a scalar UDF field the way the field is CONFIGURED, not the way a profile
    /// author guessed. Reads <c>TmX_Tenant_Field_Setup.Field_Lookup_Type</c> for the field:
    /// when it is set the field is lookup-backed (its stored value is a TmX_Lookup Id) so we
    /// emit the row's Hidden_Value; when it is blank the field carries a literal, returned
    /// as-is. Mirrors <c>AutoFlagEvaluator</c>'s config-driven normalisation.
    /// </summary>
    private async Task<string?> ResolveUdfAutoAsync(MapRun run, string? key, CancellationToken ct)
    {
        var raw = Raw(run.Detail, key);
        if (raw is null || string.IsNullOrWhiteSpace(key)) return raw;

        var lookupType = await FieldLookupTypeAsync(run, key!, ct);
        if (string.IsNullOrWhiteSpace(lookupType)) return raw;   // literal field — no lookup association

        // Lookup-backed: stored value is the TmX_Lookup Id -> resolve to its Hidden_Value.
        return int.TryParse(raw, out var id) ? await HiddenAsync(id, run, ct) : raw;
    }

    /// <summary>Field_Lookup_Type for a field name (cached per run). Non-empty ⇒ lookup-backed.</summary>
    private async Task<string?> FieldLookupTypeAsync(MapRun run, string fieldName, CancellationToken ct)
    {
        if (run.FieldLookupTypes.TryGetValue(fieldName, out var cached)) return cached;
        var lt = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.FieldName == fieldName && f.FieldLookupType != null)
            .Select(f => f.FieldLookupType)
            .FirstOrDefaultAsync(ct);
        run.FieldLookupTypes[fieldName] = lt;
        return lt;
    }

    private async Task<string?> HiddenAsync(int id, MapRun run, CancellationToken ct)
    {
        if (run.HiddenCache.TryGetValue(id, out var cached)) return cached;
        var val = await _db.Lookups.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.HiddenValue)
            .FirstOrDefaultAsync(ct);
        run.HiddenCache[id] = val;
        return val;
    }

    private static List<int> GridIds(JsonElement? cust, string? grid, string? param)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(grid) || string.IsNullOrWhiteSpace(param)) return list;
        if (cust is not { ValueKind: JsonValueKind.Object }) return list;
        if (!cust.Value.TryGetProperty(grid, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var rowEl in arr.EnumerateArray())
            if (rowEl.ValueKind == JsonValueKind.Object && TryGetId(rowEl, param) is int id) list.Add(id);
        return list;
    }

    private static string? SnapshotValue(TransactionCustomerSnapshot? snap, string? name) => (snap is null ? null : name) switch
    {
        "CustomerCode"            => snap!.CustomerCode,
        "CustomerName"            => snap!.CustomerName,
        "CustomerTitle"           => snap!.CustomerTitle,
        "NationalIdentifierValue" => snap!.NationalIdentifierValue,
        _                         => null,
    };

    // ---- Transforms ------------------------------------------------------------

    private (string? value, string? castTo) ApplyTransforms(string? val, List<TransformSpec>? transforms, MapRun run)
    {
        string? castTo = null;
        if (transforms is null) return (val, castTo);

        foreach (var t in transforms)
        {
            switch ((t.Op ?? "").ToLowerInvariant())
            {
                case "default":
                    if (string.IsNullOrEmpty(val)) val = t.Value.HasValue ? ElemToString(t.Value.Value) : val;
                    break;
                case "const":
                    val = t.Value.HasValue ? ElemToString(t.Value.Value) : val;
                    break;
                case "trim":  val = val?.Trim(); break;
                case "upper": val = val?.ToUpperInvariant(); break;
                case "lower": val = val?.ToLowerInvariant(); break;
                case "valuemap":
                    // Unmapped keys become null (chain a 'default' op for a fallback).
                    val = (val is not null && t.Map is not null && t.Map.TryGetValue(val, out var mapped)) ? mapped : null;
                    break;
                case "dateformat":
                    val = ReformatDate(val, t.From, t.To ?? run.Profile.Settings.DefaultDateFormat);
                    break;
                case "cast":
                    castTo = (t.To ?? "").ToLowerInvariant();
                    break;
                default:
                    _log.LogWarning("Onboarding mapping: unknown transform op '{Op}' — ignored.", t.Op);
                    break;
            }
        }
        return (val, castTo);
    }

    private static string? ReformatDate(string? val, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(val) || string.IsNullOrWhiteSpace(to)) return val;
        DateTime dt;
        var ok = !string.IsNullOrWhiteSpace(from)
            ? DateTime.TryParseExact(val, from, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)
            : DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        return ok ? dt.ToString(to, CultureInfo.InvariantCulture) : val;
    }

    private static JsonNode? BuildLeaf(string? val, string? castTo)
    {
        if (val is null) return null;
        switch (castTo)
        {
            case "int":
                return long.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? JsonValue.Create(l) : JsonValue.Create(val);
            case "decimal":
                return decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d) : JsonValue.Create(val);
            case "bool":
                var b = val is "1" or "true" or "True" or "TRUE" or "Y" or "y" or "yes" or "Yes";
                return JsonValue.Create(b);
            default:
                return JsonValue.Create(val);
        }
    }

    // ---- Target-path setter ----------------------------------------------------

    private static void SetPath(JsonObject root, string dotted, JsonNode? leaf, bool alwaysSet, MapRun run)
    {
        if (leaf is null && !alwaysSet && !run.Profile.Settings.EmitNulls) return;

        var parts = dotted.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var obj = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (obj[parts[i]] is JsonObject child) { obj = child; }
            else { var created = new JsonObject(); obj[parts[i]] = created; obj = created; }
        }
        obj[parts[^1]] = leaf;
    }

    // ---- JSON helpers ----------------------------------------------------------

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); } catch { return null; }
    }

    private static string? Raw(JsonElement? obj, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || obj is not { ValueKind: JsonValueKind.Object }
            || !obj.Value.TryGetProperty(key, out var el)) return null;
        var s = el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : el.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? TryGetId(JsonElement? obj, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || obj is not { ValueKind: JsonValueKind.Object }
            || !obj.Value.TryGetProperty(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i : null,
            JsonValueKind.String => int.TryParse(el.GetString(), out var i) ? i : null,
            _ => null,
        };
    }

    private static string? TryGetIdRaw(JsonElement obj, string? key)
        => TryGetId(obj, key) is int id ? id.ToString(CultureInfo.InvariantCulture) : Raw(obj, key);

    private static string? ElemToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : e.GetRawText(),
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText(),
    };

    // ---- Per-run state ---------------------------------------------------------

    private sealed class MapRun
    {
        public MappingProfile Profile { get; }
        public OnboardingMappingContext Ctx { get; }
        public JsonElement? Detail { get; }
        public JsonElement? Cust { get; }
        public Dictionary<int, string?> HiddenCache { get; }
        public Dictionary<string, string?> FieldLookupTypes { get; }

        public MapRun(MappingProfile profile, OnboardingMappingContext ctx,
            JsonElement? detail, JsonElement? cust, Dictionary<int, string?> hiddenCache,
            Dictionary<string, string?> fieldLookupTypes)
        {
            Profile = profile; Ctx = ctx; Detail = detail; Cust = cust;
            HiddenCache = hiddenCache; FieldLookupTypes = fieldLookupTypes;
        }
    }

    private sealed class RowScope
    {
        public JsonElement Row { get; }
        public string? Primary { get; }
        public bool Hydrated { get; }
        public string? Param { get; }
        public RowScope(JsonElement row, string? primary, bool hydrated, string? param)
        { Row = row; Primary = primary; Hydrated = hydrated; Param = param; }
    }
}
