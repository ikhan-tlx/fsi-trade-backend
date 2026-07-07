using System.Collections.Concurrent;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm.Mapping;

/// <summary>
/// Loads onboarding mapping profiles from disk and selects the active one by
/// <c>Integration:Onboarding:MappingProfile</c>. Files live in
/// <c>Integration:Onboarding:MappingDirectory</c> (default "Mappings/onboarding",
/// relative to the content root, or an absolute path) as
/// <c>onboarding.{name}.mapping.json</c>.
///
/// Behaviour:
/// <list type="bullet">
///   <item>Profiles are cached after first load (per name). Set
///         <c>MappingHotReload=true</c> to re-read on every call (non-prod tuning).</item>
///   <item><c>metadata.extends</c> is resolved by deep-merging this profile over
///         the named base (child rules override base rules with the same target).</item>
///   <item>A missing/invalid profile throws — the caller decides fallback. The
///         onboarding submit only asks for a profile when MappingProfile is set,
///         so misconfiguration surfaces immediately on the first submit.</item>
/// </list>
/// </summary>
public sealed class OnboardingMappingProvider : IOnboardingMappingProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly FccmOnboardingOptions _opt;
    private readonly IHostEnvironment       _env;
    private readonly ILogger<OnboardingMappingProvider> _log;
    private readonly ConcurrentDictionary<string, MappingProfile> _cache = new(StringComparer.OrdinalIgnoreCase);

    public OnboardingMappingProvider(
        IOptions<IntegrationOptions>        integration,
        IHostEnvironment                    env,
        ILogger<OnboardingMappingProvider>  log)
    {
        _opt = integration.Value.Onboarding;
        _env = env;
        _log = log;
    }

    public MappingProfile? GetActive()
        => string.IsNullOrWhiteSpace(_opt.MappingProfile) ? null : Get(_opt.MappingProfile);

    public MappingProfile Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Mapping profile name is empty.", nameof(name));

        if (!_opt.MappingHotReload && _cache.TryGetValue(name, out var cached))
            return cached;

        var profile = LoadWithInheritance(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _cache[name] = profile;
        return profile;
    }

    private MappingProfile LoadWithInheritance(string name, HashSet<string> visiting)
    {
        if (!visiting.Add(name))
            throw new InvalidOperationException($"Circular 'extends' chain in onboarding mapping profiles at '{name}'.");

        var profile = LoadFile(name);
        var baseName = profile.Metadata.Extends;
        if (!string.IsNullOrWhiteSpace(baseName))
        {
            var baseProfile = LoadWithInheritance(baseName!, visiting);
            profile = Merge(baseProfile, profile);
        }
        return profile;
    }

    private MappingProfile LoadFile(string name)
    {
        var dir = _opt.MappingDirectory;
        if (string.IsNullOrWhiteSpace(dir)) dir = "Mappings/onboarding";
        var baseDir = Path.IsPathRooted(dir) ? dir : Path.Combine(_env.ContentRootPath, dir);
        var path = Path.Combine(baseDir, $"onboarding.{name}.mapping.json");

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Onboarding mapping profile '{name}' not found at '{path}'. " +
                $"Check Integration:Onboarding:MappingProfile / MappingDirectory.", path);

        try
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<MappingProfile>(json, JsonOpts)
                          ?? throw new InvalidOperationException($"Mapping profile '{name}' deserialized to null.");
            _log.LogInformation(
                "Loaded onboarding mapping profile '{Name}' (contract={Contract}, version={Version}, fields={Fields}, arrays={Arrays}) from {Path}.",
                name, profile.Metadata.TargetContract, profile.Metadata.Version,
                profile.Fields.Count, profile.Arrays.Count, path);
            return profile;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Onboarding mapping profile '{name}' at '{path}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Deep-merge <paramref name="child"/> over <paramref name="baseProfile"/>.</summary>
    private static MappingProfile Merge(MappingProfile baseProfile, MappingProfile child)
    {
        var merged = new MappingProfile
        {
            Metadata = child.Metadata,
            Settings = new MappingSettings
            {
                EmitNulls           = child.Settings.EmitNulls || baseProfile.Settings.EmitNulls,
                DefaultDateFormat   = child.Settings.DefaultDateFormat   ?? baseProfile.Settings.DefaultDateFormat,
                ApplicationIdFormat = child.Settings.ApplicationIdFormat ?? baseProfile.Settings.ApplicationIdFormat,
            },
            Constants = new Dictionary<string, JsonElement>(baseProfile.Constants, StringComparer.OrdinalIgnoreCase),
        };
        foreach (var kv in child.Constants) merged.Constants[kv.Key] = kv.Value;

        // Fields: base first, then child overrides by Target (else appended).
        var fields = baseProfile.Fields.ToDictionary(f => f.Target, f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var f in child.Fields) fields[f.Target] = f;
        merged.Fields = fields.Values.ToList();

        // Arrays: same override-by-Target semantics.
        var arrays = baseProfile.Arrays.ToDictionary(a => a.Target, a => a, StringComparer.OrdinalIgnoreCase);
        foreach (var a in child.Arrays) arrays[a.Target] = a;
        merged.Arrays = arrays.Values.ToList();

        return merged;
    }
}
