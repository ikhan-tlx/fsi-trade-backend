using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetGroupedByCulture;

/// <summary>
/// Slice 11 v2 — returns the lookup catalogue as a dictionary keyed by
/// <c>lookupType</c>, with lean row records (id / code / label / parent).
///
/// Wins over v1:
/// <list type="bullet">
///   <item>~60% smaller payload — no <c>lookupType</c> repeated on every
///         row, no unused <c>lookupName</c> / <c>localeLabel</c>.</item>
///   <item>FE no longer needs to client-side group via
///         <c>normalizeLookupResponse</c> — direct dict access:
///         <c>lookups["Country"]</c>.</item>
///   <item>Sort order is implicit in the array (server sorts before
///         grouping).</item>
/// </list>
///
/// Same cross-tenant cascade + culture rules as v1.
/// </summary>
public record GetLookupsGroupedByCultureQuery(string Culture)
    : IRequest<LookupCatalogueDto>;

public class LookupCatalogueDto
{
    /// <summary>Echoes back the requested culture so the FE can cache by key.</summary>
    public string culture { get; set; } = "en";

    /// <summary>
    /// Dictionary keyed by <c>LookupType</c>. Order of keys is alphabetical;
    /// order of rows within each list is by <c>Sort_Order</c> then
    /// <c>Visible_Value</c>.
    /// </summary>
    public Dictionary<string, List<LookupOptionDto>> lookups { get; set; } = new();
}

/// <summary>
/// Lean per-row record. <c>lookupType</c> isn't repeated here — it's the
/// dictionary key in <see cref="LookupCatalogueDto.lookups"/>.
/// </summary>
public class LookupOptionDto
{
    /// <summary>Primary key from TmX_Lookup. Use when storing a numeric FK on a transaction row.</summary>
    public int     id     { get; set; }

    /// <summary>Semantic code (e.g. "PK", "AIR"). Used in field formulas and integration payloads.</summary>
    public string? code   { get; set; }

    /// <summary>Display label for the dropdown. Culture-resolved server-side.</summary>
    public string? label  { get; set; }

    /// <summary>Parent lookup id for cascading dropdowns (e.g. State depends on Country). Null for flat lookups.</summary>
    public int?    parent { get; set; }
}
