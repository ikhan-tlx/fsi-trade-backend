using System.Text.Json;
using System.Text.Json.Serialization;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm.Mapping;

/// <summary>
/// In-memory model of an onboarding mapping profile (deserialized from
/// <c>onboarding.{name}.mapping.json</c>). Describes how to build an
/// environment-specific FCCM onboarding payload from the internal KYC data,
/// declaratively — see docs/onboarding-mapping/ONBOARDING_MAPPING_DESIGN.md and
/// mapping.schema.json. Bound with PropertyNameCaseInsensitive = true.
/// </summary>
public sealed class MappingProfile
{
    public MappingMetadata Metadata { get; set; } = new();
    public MappingSettings Settings { get; set; } = new();
    public Dictionary<string, JsonElement> Constants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FieldRule> Fields { get; set; } = new();
    public List<ArrayRule> Arrays { get; set; } = new();
}

public sealed class MappingMetadata
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? TargetContract { get; set; }
    public string? Version { get; set; }
    /// <summary>Optional base profile to inherit from; this profile's rules override the base.</summary>
    public string? Extends { get; set; }
}

public sealed class MappingSettings
{
    /// <summary>If false, targets that resolve to null are omitted from the payload.</summary>
    public bool EmitNulls { get; set; }
    /// <summary>Default output date pattern (unused unless a dateFormat transform omits its own).</summary>
    public string? DefaultDateFormat { get; set; }
    /// <summary>Format string for context.applicationId; {0} = applicant/customer code.</summary>
    public string? ApplicationIdFormat { get; set; }
}

/// <summary>A scalar target binding: target path ← source, optionally transformed.</summary>
public sealed class FieldRule
{
    public string Target { get; set; } = "";
    public SourceSpec From { get; set; } = new();
    public List<TransformSpec>? Transform { get; set; }
    public bool? OmitWhenNull { get; set; }
    public string? Description { get; set; }
}

/// <summary>An array-valued target builder: one synthesized element or one per source grid row (recursive).</summary>
public sealed class ArrayRule
{
    public string Target { get; set; } = "";
    /// <summary>"single" | "perRow".</summary>
    public string Mode { get; set; } = "single";
    public RowSource? Rows { get; set; }
    public bool OmitWhenEmpty { get; set; } = true;
    public ElementSpec Element { get; set; } = new();
    public string? Description { get; set; }
}

public sealed class RowSource
{
    /// <summary>TmX_Customer_Master udf grid table name, e.g. TmxKYCSPOR.</summary>
    public string Grid { get; set; } = "";
    /// <summary>Row key holding the Lookup_ID (when hydrate=hidden) or the raw value.</summary>
    public string? Param { get; set; }
    /// <summary>"raw" = use the row value as-is; "hidden" = resolve Lookup_ID via TmX_Lookup.Hidden_Value.</summary>
    public string Hydrate { get; set; } = "hidden";
}

public sealed class ElementSpec
{
    public List<FieldRule> Fields { get; set; } = new();
    public List<ArrayRule> Arrays { get; set; } = new();
}

/// <summary>Where a value comes from. See mapping.schema.json for kind semantics.</summary>
public sealed class SourceSpec
{
    /// <summary>const | context | snapshot | udf.direct | udf.hidden | grid.maxScore | grid.first | row.value | row.hidden | expression.</summary>
    public string Kind { get; set; } = "const";
    public JsonElement? Value { get; set; }
    public string? Ref { get; set; }
    public string? Name { get; set; }
    public string? Key { get; set; }
    public string? Grid { get; set; }
    public string? Param { get; set; }
    public string? RowKey { get; set; }
    public string? Expr { get; set; }
}

/// <summary>One transform step. Applied in list order. See mapping.schema.json.</summary>
public sealed class TransformSpec
{
    /// <summary>default | trim | upper | lower | dateFormat | valueMap | cast | const.</summary>
    public string Op { get; set; } = "";
    public JsonElement? Value { get; set; }
    /// <summary>Input date pattern for dateFormat.</summary>
    public string? From { get; set; }
    /// <summary>Output date pattern for dateFormat; target type ("int"/"bool"/"decimal"/"string") for cast.</summary>
    public string? To { get; set; }
    /// <summary>Lookup dictionary for valueMap (string values).</summary>
    public Dictionary<string, string?>? Map { get; set; }
}

/// <summary>Loads + caches mapping profiles and selects the active one by config.</summary>
public interface IOnboardingMappingProvider
{
    /// <summary>The profile named by <c>Integration:Onboarding:MappingProfile</c> (or null if none configured).</summary>
    MappingProfile? GetActive();

    /// <summary>Load a specific profile by logical name (resolves inheritance + caches).</summary>
    MappingProfile Get(string name);
}
