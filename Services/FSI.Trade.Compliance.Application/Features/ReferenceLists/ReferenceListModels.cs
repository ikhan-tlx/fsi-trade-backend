namespace FSI.Trade.Compliance.Application.Features.ReferenceLists;

/// <summary>DTOs for the normalized Reference-List module (Types · Values · M:N).</summary>

public class UdfFieldDto
{
    public string  key       { get; set; } = "";
    public string  label     { get; set; } = "";
    public string  type      { get; set; } = "string";   // string | number | enum
    public bool    required  { get; set; }
    public double? min       { get; set; }
    public double? max       { get; set; }
    public int?    maxLength { get; set; }
    public List<string>? options { get; set; }
}

public class DomainDto
{
    public string  key   { get; set; } = "";   // the Lookup_Type ("" = literal/custom)
    public string  label { get; set; } = "";
}

public class TypeChipDto
{
    public long   id   { get; set; }
    public string code { get; set; } = "";
    public string name { get; set; } = "";
}

public class ReferenceTypeSummaryDto
{
    public long    id          { get; set; }
    public string  code        { get; set; } = "";
    public string  name        { get; set; } = "";
    public string? description { get; set; }
    public string? lookupType  { get; set; }
    public string  domainLabel { get; set; } = "";
    public int     valueCount  { get; set; }
    public int     usedByCount { get; set; }
    public bool    active      { get; set; }
    public string  source      { get; set; } = "user";
}

public class TypeMemberDto
{
    public long   valueId { get; set; }
    public string code    { get; set; } = "";
    public string name    { get; set; } = "";
    public bool   active  { get; set; } = true;
}

public class ReferenceTypeDetailDto
{
    public long    id          { get; set; }
    public string  code        { get; set; } = "";
    public string  name        { get; set; } = "";
    public string? description { get; set; }
    public string? lookupType  { get; set; }
    public string  domainLabel { get; set; } = "";
    public bool    active      { get; set; } = true;
    public string  source      { get; set; } = "user";
    public int     usedByCount { get; set; }
    public List<UdfFieldDto>  schema  { get; set; } = new();
    public List<TypeMemberDto> members { get; set; } = new();
}

public class ReferenceValueSummaryDto
{
    public long    id          { get; set; }
    public string  code        { get; set; } = "";
    public string? name        { get; set; }
    public string? lookupType  { get; set; }
    public string  domainLabel { get; set; } = "";
    public bool    active      { get; set; }
    public List<TypeChipDto> categories { get; set; } = new();
}

public class ReferenceValueDetailDto
{
    public long    id          { get; set; }
    public string  code        { get; set; } = "";
    public string? name        { get; set; }
    public string? description { get; set; }
    public string? lookupType  { get; set; }
    public string  domainLabel { get; set; } = "";
    public bool    active      { get; set; } = true;
    public Dictionary<string, object?> udf { get; set; } = new();
    public List<long> typeIds   { get; set; } = new();
    public List<TypeChipDto>  categories { get; set; } = new();
    public List<UdfFieldDto>  schema     { get; set; } = new();  // union across selected types
}

public class LookupPickDto
{
    public string code { get; set; } = "";  // Hidden_Value
    public string name { get; set; } = "";  // Visible_Value
}

public class ReferenceRuleUsageDto
{
    public int    flagId     { get; set; }
    public string name       { get; set; } = "";
    public string op         { get; set; } = "In";
    public int    conditions { get; set; }
    public string status     { get; set; } = "";
}

public class FieldDomainDto
{
    public string  fieldCode   { get; set; } = "";
    public string  label       { get; set; } = "";
    public string? lookupType  { get; set; }
    public string  domainLabel { get; set; } = "";
}

/// <summary>Shared helper — a friendly label for a Lookup_Type "domain".</summary>
public static class DomainLabels
{
    public static string For(string? lookupType)
    {
        if (string.IsNullOrWhiteSpace(lookupType)) return "Custom";
        var u = lookupType.ToUpperInvariant();
        // Distinguish the two nationality catalogues explicitly BEFORE the generic country rule —
        // otherwise "Nationality" and "TradeNationality" both collapse to "Country" (duplicate label).
        if (u == "NATIONALITY")                            return "Nationality";
        if (u == "TRADENATIONALITY")                       return "Trade Nationality";
        if (u.Contains("COUNTRY") || u.Contains("NATION")) return "Country";
        // Distinguish the two HS catalogues explicitly BEFORE the generic HS rule — otherwise both
        // "HSCodeList" and "HSPriceList" collapse to "HS code" (duplicate label, price list missing).
        if (u == "HSCODELIST")                             return "HS Code List";
        if (u == "HSPRICELIST")                            return "HS Price List";
        if (u.Contains("HS") || u.Contains("GOOD"))        return "HS code";
        if (u.Contains("PORT"))                            return "Port";
        if (u.Contains("CURREN"))                          return "Currency";
        // Humanize: split on _ / camelCase, title-case.
        var parts = lookupType.Replace('_', ' ').Trim();
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(parts.ToLowerInvariant());
    }
}
