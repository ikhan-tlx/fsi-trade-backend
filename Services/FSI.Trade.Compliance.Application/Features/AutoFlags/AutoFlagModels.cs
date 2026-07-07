namespace FSI.Trade.Compliance.Application.Features.AutoFlags;

/// <summary>Shared DTOs for the Auto Flags admin screen (/admin/auto-flags).
/// Camel-cased to match the FE <c>auto-flags-api.ts</c> contract.</summary>

public class AutoFlagRuleDto
{
    public int     ruleId           { get; set; }
    public int     ruleGroup        { get; set; } = 1;
    public int     evaluationOrder  { get; set; } = 1;
    public string  leftSource       { get; set; } = "Trade";
    public string? leftFieldCode    { get; set; }
    public string  @operator        { get; set; } = "In";
    public string  rightOperandType { get; set; } = "ReferenceList";
    public string? rightSource      { get; set; }
    public string? rightFieldCode   { get; set; }
    public string? staticValue      { get; set; }
    public string? referenceListCode{ get; set; }
    public string? aggregateSpec    { get; set; }
    /// <summary>Named metric handler for an Aggregate/Derived left operand (e.g. "AGGREGATE").</summary>
    public string? handlerKey       { get; set; }
    /// <summary>Optional scale on the RIGHT operand: value * (unit=="Percent" ? f/100 : f).</summary>
    public decimal? rightFactor     { get; set; }
    public string? rightFactorUnit  { get; set; }   // "Multiplier" | "Percent"
    public string  logicalOperator  { get; set; } = "AND";
}

public class AutoFlagSummaryDto
{
    public int       flagId    { get; set; }
    public string    flagCode  { get; set; } = "";
    public string    name      { get; set; } = "";
    public string    severity  { get; set; } = "";
    public string    status    { get; set; } = "Draft";   // Draft | Active
    public List<string> products { get; set; } = new();   // product names (scope chips)
    public int       ruleCount { get; set; }
    public string?   updatedBy { get; set; }
    public int?      versionNo { get; set; }
    public string?   updatedAt { get; set; }
}

/// <summary>One flag-scope entry. tabId = null means "all sub-products of the product";
/// productId = null means "all products" (global). Maps 1:1 to TmX_Flag_Scope.</summary>
public class ScopeEntryDto
{
    public int?  productId { get; set; }
    public int?  tabId     { get; set; }
}

public class AutoFlagDetailDto : AutoFlagSummaryDto
{
    public string?            description     { get; set; }
    public string?            category        { get; set; }
    /// <summary>Legacy product-level scope (kept for back-compat; product ids only).</summary>
    public List<int>          scopeProductIds { get; set; } = new();
    /// <summary>Full scope incl. sub-product (tab). Preferred over scopeProductIds.</summary>
    public List<ScopeEntryDto> scopes         { get; set; } = new();
    /// <summary>FALSE (default) = one flag summarising all matches; TRUE = one flag per matching value.</summary>
    public bool               raisePerMatch   { get; set; }
    public List<AutoFlagRuleDto> rules        { get; set; } = new();
}

public class OperandOptionDto
{
    public string  fieldCode   { get; set; } = "";
    public string  label       { get; set; } = "";
    public string  source      { get; set; } = "Trade";   // Trade | KYC
    public string  dataType    { get; set; } = "text";     // code | number
    public string? fieldType   { get; set; }               // Dropdown | SearchableDropdown | Amount | Number
    /// <summary>TRUE when the field is a column inside a repeating grid/table (its
    /// parent is a grid root). Such a field can hold many values (one per row), so a
    /// rule on it matches if ANY row hits — the FE surfaces this so authors know it fans out.</summary>
    public bool    isMultiValue { get; set; }
    // Grouping context for the field-picker (Tab → section → fields).
    public int?    productId   { get; set; }
    public string? productName { get; set; }
    public int?    tabId       { get; set; }
    public string? tabName     { get; set; }
    public string? section     { get; set; }   // humanised Field_Table_Name
}

public class ReferenceListOptionDto
{
    public string code { get; set; } = "";
    public string name { get; set; } = "";
}

/// <summary>Aggregate/derived metric catalogue entry — drives the rule-builder's
/// Aggregate tab param form (config-driven; no hardcoded metrics in the FE).</summary>
public class MetricDefDto
{
    public string key      { get; set; } = "";
    public string label    { get; set; } = "";
    public string kind     { get; set; } = "";   // Aggregate | Derived
    public string dataType { get; set; } = "";   // number | date | boolean | code
    public List<MetricParamDefDto> @params { get; set; } = new();
}

public class MetricParamDefDto
{
    public string  name        { get; set; } = "";
    public string  label       { get; set; } = "";
    public string  type        { get; set; } = "";   // enum | field | number | list
    public string? fieldFilter { get; set; }
    public bool    required    { get; set; }
    public List<string>? options { get; set; }
}

/// <summary>Products with their sub-product tabs — for the scope editor tree.</summary>
public class SubProductGroupDto
{
    public int    productId   { get; set; }
    public string productName { get; set; } = "";
    public List<SubProductTabDto> tabs { get; set; } = new();
}

public class SubProductTabDto
{
    public int    tabId   { get; set; }
    public string tabName { get; set; } = "";
}

public class AutoFlagTestResultDto
{
    public bool         wouldRaise    { get; set; }
    public List<int>    matchedRules  { get; set; } = new();
    public string?      detail        { get; set; }
    // 2026_06_061 — Test panel surfaces the per-match outcome + the dev "proof".
    public bool         perMatch      { get; set; }
    public List<string> matchedValues { get; set; } = new();
    public string?      proof         { get; set; }
}

/// <summary>Create/Update payload (FE sends Partial&lt;AutoFlagDetail&gt;).</summary>
public class AutoFlagWriteModel
{
    public string?  flagCode        { get; set; }
    public string?  name            { get; set; }
    public string?  description     { get; set; }
    public string?  severity        { get; set; }      // Critical|High|Medium|Low|Info
    public List<int>? scopeProductIds { get; set; }     // legacy product-level scope (back-compat)
    public List<ScopeEntryDto>? scopes { get; set; }    // preferred: product + sub-product (tab) scope
    public bool?    raisePerMatch   { get; set; }       // null = leave unchanged on update; defaults false on create
    public List<AutoFlagRuleDto>? rules { get; set; }
}
