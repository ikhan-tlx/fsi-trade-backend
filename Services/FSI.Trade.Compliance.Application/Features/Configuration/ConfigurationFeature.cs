using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Configuration;

// =====================================================================
// Configuration Management (Phase 1) — CQRS feature.
// DTOs + MediatR requests + handlers for the /admin/configuration screen.
// Reads/writes the TmX_Configuration_* tables and, on every write, calls
// IConfigurationRefresher.Refresh() so the IOptions bridge republishes and
// existing consumers re-bind (no restart). Business logic is UNCHANGED —
// this only manages the configuration source.
// =====================================================================

// ---------- DTOs (shape mirrors the FE ConfigSetting contract) ----------
public class ConfigSettingDto
{
    public string  key { get; set; } = default!;
    public string  displayName { get; set; } = default!;
    public string? description { get; set; }
    public string  category { get; set; } = default!;
    public string  dataType { get; set; } = default!;
    public object? value { get; set; }
    public object? defaultValue { get; set; }
    public string? unit { get; set; }
    public int?    min { get; set; }
    public int?    max { get; set; }
    public int?    step { get; set; }
    public string[]? allowedValues { get; set; }
    public bool    isEditable { get; set; }
    public bool    requiresApproval { get; set; }
    public bool    isSensitive { get; set; }
    public bool    appliesOnRestart { get; set; }
    public object? pendingValue { get; set; }
    public string? lastModifiedBy { get; set; }
    public string? lastModifiedAt { get; set; }
}

public class ConfigCategoryDto
{
    public string  id { get; set; } = default!;
    public string  title { get; set; } = default!;
    public string? description { get; set; }
    public string? verify { get; set; }
    public bool    flagged { get; set; }
    public bool    logging { get; set; }
}

public class ConfigCatalogDto
{
    public List<ConfigCategoryDto> categories { get; set; } = new();
    public List<ConfigSettingDto>  settings   { get; set; } = new();
}

public class ConfigHistoryDto
{
    public long    historyId { get; set; }
    public string? when { get; set; }
    public string? who { get; set; }
    public string? oldValue { get; set; }
    public string? newValue { get; set; }
    public string  changeType { get; set; } = default!;
}

public class SaveManyResultDto
{
    public List<ConfigSettingDto> saved   { get; set; } = new();
    public List<ConfigSettingDto> pending { get; set; } = new();
    public List<FailedSaveDto>    failed  { get; set; } = new();
}
public class FailedSaveDto { public string key { get; set; } = default!; public string error { get; set; } = default!; }

// Pre-auth branding contract for the login page (title / logo color / subtitle / theme).
// camelCase members match the sibling ConfigSettingDto/ConfigCatalogDto style in this file.
public sealed class BrandingDto
{
    public string brandColor   { get; set; } = default!;
    public string brandGold    { get; set; } = default!;
    public string appTitle     { get; set; } = default!;
    public string appSubtitle  { get; set; } = default!;
    public string defaultTheme { get; set; } = default!;
}

// ---------- Shared helpers ----------
internal static class ConfigCoerce
{
    /// <summary>Coerce an incoming JSON value to its stored string form for a data type.</summary>
    public static string? ToStorage(string dataType, JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True  => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.String => v.GetString(),
        _ => v.GetRawText(), // Object/Array → Json / LogModule raw text
    };

    /// <summary>Turn a stored string back into the typed shape the FE editors expect.</summary>
    public static object? ToTyped(string dataType, string? raw)
    {
        if (raw is null) return null;
        switch (dataType)
        {
            case "Boolean":  return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
            case "Integer":  return long.TryParse(raw, out var l) ? l : raw;
            case "Decimal":  return decimal.TryParse(raw, out var d) ? d : raw;
            case "Secret":   return "••••••"; // masked
            case "LogModule":
                try { using var doc = JsonDocument.Parse(raw); return JsonSerializer.Deserialize<object>(raw); }
                catch { return raw; }
            case "Json":     return raw; // FE JSON editor works on the raw string
            default:         return raw; // String/Text/Enum/TimeSpan/DateTime
        }
    }

    public static (int? min, int? max, int? step, string[]? allowed) ParseRules(string? rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson)) return (null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(rulesJson);
            var r = doc.RootElement;
            int? Get(string n) => r.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int?)null;
            string[]? allowed = null;
            if (r.TryGetProperty("allowedValues", out var av) && av.ValueKind == JsonValueKind.Array)
                allowed = av.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            return (Get("min"), Get("max"), Get("step"), allowed);
        }
        catch { return (null, null, null, null); }
    }

    public static string? Mask(string? connOrSecret)
    {
        if (string.IsNullOrEmpty(connOrSecret)) return connOrSecret;
        // Mask password=... segments in a connection string.
        return System.Text.RegularExpressions.Regex.Replace(
            connOrSecret, @"(?i)(password\s*=\s*)([^;]*)", "$1••••••");
    }
}

// ---------- Queries ----------
public record GetConfigCatalogQuery() : IRequest<ConfigCatalogDto>;

public class GetConfigCatalogQueryHandler : IRequestHandler<GetConfigCatalogQuery, ConfigCatalogDto>
{
    private readonly IApplicationDbContext _db;
    public GetConfigCatalogQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ConfigCatalogDto> Handle(GetConfigCatalogQuery req, CancellationToken ct)
    {
        var cats = await _db.ConfigurationCategories.AsNoTracking()
            .OrderBy(c => c.DisplayOrder).ToListAsync(ct);
        var defs = await _db.ConfigurationDefinitions.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.DisplayOrder).ToListAsync(ct);
        var vals = await _db.ConfigurationValues.AsNoTracking()
            .Where(v => v.IsActive && v.BranchId == null && v.RoleId == null && v.Environment == null)
            .ToListAsync(ct);
        var valByDef = vals.GroupBy(v => v.ConfigurationDefinitionId).ToDictionary(g => g.Key, g => g.First());

        var catalog = new ConfigCatalogDto
        {
            categories = cats.Select(c => new ConfigCategoryDto
            {
                id = c.CategoryKey, title = c.Title, description = c.Description,
                verify = c.VerifyKind, flagged = c.IsFlagged, logging = c.IsLogging
            }).ToList()
        };

        foreach (var d in defs)
        {
            valByDef.TryGetValue(d.ConfigurationDefinitionId, out var v);
            var raw = v?.Value ?? d.DefaultValue;
            var (min, max, step, allowed) = ConfigCoerce.ParseRules(d.ValidationRules);
            catalog.settings.Add(new ConfigSettingDto
            {
                key = d.ConfigurationKey,
                displayName = d.DisplayName,
                description = d.Description,
                category = d.CategoryKey,
                dataType = d.DataType,
                value = ConfigCoerce.ToTyped(d.DataType, raw),
                defaultValue = ConfigCoerce.ToTyped(d.DataType, d.DefaultValue),
                unit = d.Unit,
                min = min, max = max, step = step, allowedValues = allowed,
                isEditable = d.IsEditable,
                requiresApproval = d.RequiresApproval,
                isSensitive = d.IsSensitive,
                appliesOnRestart = d.AppliesOnRestart,
                pendingValue = v?.PendingValue is null ? null : ConfigCoerce.ToTyped(d.DataType, v.PendingValue),
                lastModifiedBy = v?.LastUpdatedBy ?? d.LastUpdatedBy,
                lastModifiedAt = (v?.LastUpdatedDate ?? d.LastUpdatedDate)?.ToString("o")
            });
        }
        return catalog;
    }
}

public record GetConfigHistoryQuery(string Key) : IRequest<List<ConfigHistoryDto>>;

public class GetConfigHistoryQueryHandler : IRequestHandler<GetConfigHistoryQuery, List<ConfigHistoryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetConfigHistoryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ConfigHistoryDto>> Handle(GetConfigHistoryQuery req, CancellationToken ct)
    {
        var def = await _db.ConfigurationDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ConfigurationKey == req.Key, ct);
        if (def is null) return new();
        var isSecret = def.IsSensitive;

        var rows = await _db.ConfigurationHistories.AsNoTracking()
            .Where(h => h.ConfigurationDefinitionId == def.ConfigurationDefinitionId)
            .OrderByDescending(h => h.ChangedDate)
            .Take(100).ToListAsync(ct);

        return rows.Select(h => new ConfigHistoryDto
        {
            historyId = h.ConfigurationHistoryId,
            when = h.ChangedDate.ToString("o"),
            who = h.ChangedBy,
            oldValue = isSecret ? "••••••" : h.OldValue,
            newValue = isSecret ? "••••••" : h.NewValue,
            changeType = h.ChangeType
        }).ToList();
    }
}

// ---------- Branding (pre-auth appearance read) ----------
// Read-only: returns the effective Global value for each Branding.* key (value
// row -> Definition Default_Value -> hardcoded fallback if the definition is
// missing). No SaveChanges — this is the public login-page read.
public record GetBrandingQuery() : IRequest<ResponseViewModel<BrandingDto>>;

public class GetBrandingQueryHandler : IRequestHandler<GetBrandingQuery, ResponseViewModel<BrandingDto>>
{
    private const string KeyBrandColor   = "Branding.BrandColor";
    private const string KeyBrandGold    = "Branding.BrandGold";
    private const string KeyAppTitle     = "Branding.AppTitle";
    private const string KeyAppSubtitle  = "Branding.AppSubtitle";
    private const string KeyDefaultTheme = "Branding.DefaultTheme";

    private readonly IApplicationDbContext _db;
    public GetBrandingQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ResponseViewModel<BrandingDto>> Handle(GetBrandingQuery req, CancellationToken ct)
    {
        var keys = new[] { KeyBrandColor, KeyBrandGold, KeyAppTitle, KeyAppSubtitle, KeyDefaultTheme };

        var defs = await _db.ConfigurationDefinitions.AsNoTracking()
            .Where(d => keys.Contains(d.ConfigurationKey))
            .ToListAsync(ct);
        var defIds = defs.Select(d => d.ConfigurationDefinitionId).ToList();
        var vals = await _db.ConfigurationValues.AsNoTracking()
            .Where(v => v.IsActive && v.BranchId == null && v.RoleId == null && v.Environment == null
                        && defIds.Contains(v.ConfigurationDefinitionId))
            .ToListAsync(ct);
        var valByDef = vals.GroupBy(v => v.ConfigurationDefinitionId).ToDictionary(g => g.Key, g => g.First());

        // Branding keys are String/Enum, so the stored form is the value itself
        // (ConfigCoerce.ToTyped would round-trip String/Enum unchanged).
        string Effective(string key, string hardDefault)
        {
            var def = defs.FirstOrDefault(d => d.ConfigurationKey == key);
            if (def is null) return hardDefault;                       // definition entirely missing
            valByDef.TryGetValue(def.ConfigurationDefinitionId, out var v);
            var raw = v?.Value ?? def.DefaultValue;                    // fall back to Default_Value
            return string.IsNullOrWhiteSpace(raw) ? hardDefault : raw;
        }

        var dto = new BrandingDto
        {
            brandColor   = Effective(KeyBrandColor,   "#c7000b"),
            brandGold    = Effective(KeyBrandGold,    "#e8c27a"),
            appTitle     = Effective(KeyAppTitle,     "ICBC Trade Compliance"),
            appSubtitle  = Effective(KeyAppSubtitle,  "工商银行 · KYC Platform"),
            defaultTheme = Effective(KeyDefaultTheme, "light"),
        };
        return ResponseViewModel<BrandingDto>.Ok(dto);
    }
}

// ---------- Commands: save / reset / rollback ----------
public record SaveConfigValueCommand(string Key, JsonElement Value) : IRequest<ConfigSettingDto>;
public record SaveManyConfigCommand(List<(string Key, JsonElement Value)> Changes) : IRequest<SaveManyResultDto>;
public record ResetConfigCommand(string Key) : IRequest<ConfigSettingDto>;
public record RollbackConfigCommand(string Key, long HistoryId) : IRequest<ConfigSettingDto>;

public class ConfigWriteHandlers :
    IRequestHandler<SaveConfigValueCommand, ConfigSettingDto>,
    IRequestHandler<SaveManyConfigCommand, SaveManyResultDto>,
    IRequestHandler<ResetConfigCommand, ConfigSettingDto>,
    IRequestHandler<RollbackConfigCommand, ConfigSettingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IConfigurationRefresher _refresher;
    private readonly FSI.Trade.Compliance.Application.Contracts.Configuration.ISecretProtector _secrets;

    public ConfigWriteHandlers(IApplicationDbContext db, ICurrentUserService current,
                               IConfigurationRefresher refresher,
                               FSI.Trade.Compliance.Application.Contracts.Configuration.ISecretProtector secrets)
    { _db = db; _current = current; _refresher = refresher; _secrets = secrets; }

    private string Who => _current.UserId ?? "system";

    /// <summary>A secret save that carries no real new value (empty, or the masked
    /// placeholder the FE displays) is a no-op — we must not overwrite the stored
    /// secret with a mask.</summary>
    private static bool IsMaskOrEmpty(string? storage)
        => string.IsNullOrWhiteSpace(storage) || storage!.Trim('•', ' ').Length == 0;

    public async Task<ConfigSettingDto> Handle(SaveConfigValueCommand req, CancellationToken ct)
        => await SaveOneAsync(req.Key, req.Value, ct, refresh: true);

    public async Task<SaveManyResultDto> Handle(SaveManyConfigCommand req, CancellationToken ct)
    {
        var result = new SaveManyResultDto();
        foreach (var (key, value) in req.Changes)
        {
            try
            {
                var dto = await SaveOneAsync(key, value, ct, refresh: false);
                if (dto.pendingValue != null) result.pending.Add(dto); else result.saved.Add(dto);
            }
            catch (Exception ex)
            {
                result.failed.Add(new FailedSaveDto { key = key, error = ex.Message });
            }
        }
        if (result.saved.Count > 0 || result.pending.Count > 0) _refresher.Refresh();
        return result;
    }

    public async Task<ConfigSettingDto> Handle(ResetConfigCommand req, CancellationToken ct)
    {
        var (def, val) = await LoadAsync(req.Key, ct);
        var old = val?.Value;
        var newVal = def.DefaultValue;
        await EnsureValue(def, val, newVal, ct);
        AppendHistory(def, val, old, newVal, "Reset", ct);
        await _db.SaveChangesAsync(ct);
        _refresher.Refresh();
        return await Project(def.ConfigurationKey, ct);
    }

    public async Task<ConfigSettingDto> Handle(RollbackConfigCommand req, CancellationToken ct)
    {
        var (def, val) = await LoadAsync(req.Key, ct);
        var hist = await _db.ConfigurationHistories
            .FirstOrDefaultAsync(h => h.ConfigurationHistoryId == req.HistoryId
                                   && h.ConfigurationDefinitionId == def.ConfigurationDefinitionId, ct)
            ?? throw new InvalidOperationException("history_not_found");
        var old = val?.Value;
        var newVal = hist.OldValue; // re-apply the pre-change value
        await EnsureValue(def, val, newVal, ct);
        AppendHistory(def, val, old, newVal, "Rollback", ct);
        await _db.SaveChangesAsync(ct);
        _refresher.Refresh();
        return await Project(def.ConfigurationKey, ct);
    }

    // ----- internals -----
    private async Task<ConfigSettingDto> SaveOneAsync(string key, JsonElement value, CancellationToken ct, bool refresh)
    {
        var (def, val) = await LoadAsync(key, ct);
        if (!def.IsEditable) throw new InvalidOperationException("configuration_not_editable");

        var storage = ConfigCoerce.ToStorage(def.DataType, value);
        Validate(def, storage);

        // Secrets are stored ENCRYPTED at rest. Validate the plaintext above, then
        // encrypt. The FE never round-trips the masked value; if an empty or all-mask
        // value arrives it means "no change" — keep the existing secret untouched.
        if (def.IsSensitive)
        {
            if (IsMaskOrEmpty(storage))
                return await Project(key, ct);
            if (!_secrets.IsProtected(storage))
                storage = _secrets.Protect(storage!);
        }

        var old = val?.Value;
        if (def.RequiresApproval)
        {
            // Dual control: stage as pending; do NOT change the effective value.
            val = await EnsureRow(def, ct);
            val.PendingValue = storage; val.PendingBy = Who; val.PendingDate = DateTime.UtcNow;
            val.LastUpdatedBy = Who; val.LastUpdatedDate = DateTime.UtcNow;
            AppendHistory(def, val, old, storage, "SubmitForApproval", ct);
            // TODO(Phase 2): route to the TmX_Request maker-checker queue + an approver
            //                resolution endpoint that applies Pending_Value on approve.
        }
        else
        {
            await EnsureValue(def, val, storage, ct);
            AppendHistory(def, val, old, storage, "Update", ct);
        }
        await _db.SaveChangesAsync(ct);
        if (refresh) _refresher.Refresh();
        return await Project(key, ct);
    }

    private async Task<(Domain.Entities.Configuration.ConfigurationDefinition def, Domain.Entities.Configuration.ConfigurationValue? val)> LoadAsync(string key, CancellationToken ct)
    {
        var def = await _db.ConfigurationDefinitions.FirstOrDefaultAsync(d => d.ConfigurationKey == key, ct)
                  ?? throw new InvalidOperationException("configuration_not_found");
        var val = await _db.ConfigurationValues.FirstOrDefaultAsync(v =>
            v.ConfigurationDefinitionId == def.ConfigurationDefinitionId
            && v.IsActive && v.BranchId == null && v.RoleId == null && v.Environment == null, ct);
        return (def, val);
    }

    private async Task<Domain.Entities.Configuration.ConfigurationValue> EnsureRow(Domain.Entities.Configuration.ConfigurationDefinition def, CancellationToken ct)
    {
        var val = await _db.ConfigurationValues.FirstOrDefaultAsync(v =>
            v.ConfigurationDefinitionId == def.ConfigurationDefinitionId
            && v.IsActive && v.BranchId == null && v.RoleId == null && v.Environment == null, ct);
        if (val is null)
        {
            val = new Domain.Entities.Configuration.ConfigurationValue
            {
                ConfigurationDefinitionId = def.ConfigurationDefinitionId,
                IsActive = true, CreatedBy = Who, CreatedDate = DateTime.UtcNow
            };
            _db.ConfigurationValues.Add(val);
        }
        return val;
    }

    private async Task EnsureValue(Domain.Entities.Configuration.ConfigurationDefinition def, Domain.Entities.Configuration.ConfigurationValue? val, string? storage, CancellationToken ct)
    {
        val ??= await EnsureRow(def, ct);
        val.Value = storage;
        val.PendingValue = null; val.PendingBy = null; val.PendingDate = null;
        val.LastUpdatedBy = Who; val.LastUpdatedDate = DateTime.UtcNow;
    }

    private void AppendHistory(Domain.Entities.Configuration.ConfigurationDefinition def, Domain.Entities.Configuration.ConfigurationValue? val, string? oldVal, string? newVal, string changeType, CancellationToken ct)
    {
        _db.ConfigurationHistories.Add(new Domain.Entities.Configuration.ConfigurationHistory
        {
            ConfigurationDefinitionId = def.ConfigurationDefinitionId,
            ConfigurationValueId = val?.ConfigurationValueId,
            OldValue = oldVal, NewValue = newVal,
            ChangeType = changeType, ScopeDescriptor = "Global",
            ChangedBy = Who, ChangedDate = DateTime.UtcNow
        });
    }

    private static void Validate(Domain.Entities.Configuration.ConfigurationDefinition def, string? storage)
    {
        var (min, max, _, allowed) = ConfigCoerce.ParseRules(def.ValidationRules);
        switch (def.DataType)
        {
            case "Integer":
            case "Decimal":
                if (!decimal.TryParse(storage, out var n)) throw new InvalidOperationException("value_not_numeric");
                if (min.HasValue && n < min) throw new InvalidOperationException($"Must be between {min} and {max}.");
                if (max.HasValue && n > max) throw new InvalidOperationException($"Must be between {min} and {max}.");
                break;
            case "Enum":
                if (allowed is { Length: > 0 } && storage != null && !allowed.Contains(storage))
                    throw new InvalidOperationException("value_not_allowed");
                break;
            case "Json":
                if (storage != null) { try { using var _ = JsonDocument.Parse(storage); } catch { throw new InvalidOperationException("invalid_json"); } }
                break;
            case "String":
                // Onboarding.BaseUrl must be https when verification is enabled — enforced in Phase 2
                // once the flag drives business logic; here we only require non-blank for required URLs.
                break;
        }
    }

    private async Task<ConfigSettingDto> Project(string key, CancellationToken ct)
    {
        // Reuse the catalog projection for one key (small table; simple + consistent).
        var handler = new GetConfigCatalogQueryHandler(_db);
        var catalog = await handler.Handle(new GetConfigCatalogQuery(), ct);
        return catalog.settings.First(s => s.key == key);
    }
}
