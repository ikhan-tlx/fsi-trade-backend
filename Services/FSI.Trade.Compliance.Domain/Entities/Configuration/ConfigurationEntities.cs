namespace FSI.Trade.Compliance.Domain.Entities.Configuration;

// =====================================================================
// Configuration Management (Phase 1) — DB-driven application settings.
// Definition = catalogue (metadata). Value = scoped value(s) (v1: Global
// only). History = immutable change trail. Category = rail grouping.
// See CONFIGURATION_MANAGEMENT_MODULE_DESIGN_AND_PLAN.md §3.
// =====================================================================

/// <summary>Rail grouping for the admin screen (metadata only).</summary>
public class ConfigurationCategory
{
    public string  CategoryKey   { get; set; } = default!;
    public string  Title         { get; set; } = default!;
    public string? Description    { get; set; }
    public int     DisplayOrder  { get; set; }
    /// <summary>e.g. "fccm" → the category shows a Test-connection chip.</summary>
    public string? VerifyKind    { get; set; }
    public bool    IsFlagged     { get; set; }
    public bool    IsLogging     { get; set; }
}

/// <summary>One row per configurable setting (the catalogue / metadata).</summary>
public class ConfigurationDefinition
{
    public int     ConfigurationDefinitionId { get; set; }
    /// <summary>Canonical key = dotted appsettings path (drives the IOptions bridge).</summary>
    public string  ConfigurationKey { get; set; } = default!;
    public string  DisplayName      { get; set; } = default!;
    /// <summary>Short one-line summary shown under the setting name on the screen.</summary>
    public string? Description       { get; set; }
    /// <summary>Long-form "what this actually does" detail shown in the (i) info
    /// popover — describes the real runtime effect / consumer.</summary>
    public string? HelpDetail        { get; set; }
    public string  CategoryKey      { get; set; } = default!;
    /// <summary>Optional sub-group heading within a category (e.g. FCCM →
    /// "Oracle connection" / "Case defaults"). NULL = no sub-header.</summary>
    public string? Section           { get; set; }
    /// <summary>Boolean|Integer|Decimal|String|Text|Enum|Json|TimeSpan|DateTime|Secret|LogModule.</summary>
    public string  DataType         { get; set; } = default!;
    public string? DefaultValue     { get; set; }
    /// <summary>JSON: { min, max, step, allowedValues, regex, required, format }.</summary>
    public string? ValidationRules  { get; set; }
    public string? Unit             { get; set; }
    public bool    IsEditable        { get; set; } = true;
    public bool    IsSensitive       { get; set; }
    public bool    RequiresApproval  { get; set; }
    public bool    AppliesOnRestart  { get; set; }
    public bool    IsActive          { get; set; } = true;
    public int     DisplayOrder      { get; set; }
    public string?   CreatedBy       { get; set; }
    public DateTime? CreatedDate     { get; set; }
    public string?   LastUpdatedBy   { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

/// <summary>Scoped value for a definition (v1 writes only the Global row).</summary>
public class ConfigurationValue
{
    public int     ConfigurationValueId      { get; set; }
    public int     ConfigurationDefinitionId { get; set; }
    /// <summary>String-encoded value; NULL ⇒ use the definition Default_Value.</summary>
    public string? Value        { get; set; }
    public int?    BranchId     { get; set; }   // future scope (NULL = Global)
    public int?    RoleId       { get; set; }   // future scope
    public string? Environment  { get; set; }   // future scope
    // Dual-control: a change awaiting a second approver.
    public string?   PendingValue { get; set; }
    public string?   PendingBy    { get; set; }
    public DateTime? PendingDate  { get; set; }
    public bool      IsActive     { get; set; } = true;
    public string?   CreatedBy       { get; set; }
    public DateTime? CreatedDate     { get; set; }
    public string?   LastUpdatedBy   { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

/// <summary>Immutable audit row appended on every change / reset / rollback.</summary>
public class ConfigurationHistory
{
    public long   ConfigurationHistoryId    { get; set; }
    public int    ConfigurationDefinitionId { get; set; }
    public int?   ConfigurationValueId      { get; set; }
    public string? OldValue        { get; set; }
    public string? NewValue        { get; set; }
    /// <summary>Update|Reset|Create|Rollback|Deactivate|SubmitForApproval.</summary>
    public string  ChangeType      { get; set; } = default!;
    public string? ScopeDescriptor { get; set; }
    public long?   RequestId       { get; set; }
    public string? ChangedBy       { get; set; }
    public DateTime ChangedDate     { get; set; }
}
