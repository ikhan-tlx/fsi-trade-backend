using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.List;

/// <summary>
/// Slice 8 Step 6 — paged list for the admin grid. Filter by free-text
/// (matches Flag_Code / Flag_Name / Flag_Description), category, severity
/// type, and active state. Sort by name / createdDate / lastUpdatedDate.
///
/// Maps to <c>GET /api/v1/Flag</c>. Gated by <c>Flags.View</c>.
/// </summary>
public class ListFlagCatalogueQuery : PagedQuery, IRequest<PagedResult<FlagCatalogueListItemDto>>
{
    /// <summary>Optional FLAG_SEVERITY lookup ID filter.</summary>
    public int?  SeverityLkpId   { get; set; }

    /// <summary>Optional FLAG_TYPE lookup ID filter (Manual / Automated).</summary>
    public int?  FlagTypeLkpId   { get; set; }

    /// <summary>
    /// Null = both active and inactive. Mostly admins want active-only,
    /// but managing retired flags needs the off-switch.
    /// </summary>
    public bool? ActiveFlag      { get; set; }

    /// <summary>
    /// Scope filter for the "All scopes" dropdown. When set, returns flags
    /// that have a scope on this product OR an "all products" (global)
    /// scope. Combine with <see cref="TabId"/> to narrow to a sub-product
    /// tab (product-level and global scopes still match).
    /// </summary>
    public int?  ProductId       { get; set; }

    /// <summary>Optional tab narrow — only meaningful alongside <see cref="ProductId"/>.</summary>
    public int?  TabId           { get; set; }

    /// <summary>
    /// TRUE = the "All products" filter chip — return only flags that carry
    /// a global (Product_ID IS NULL) scope. Takes precedence over
    /// <see cref="ProductId"/>/<see cref="TabId"/> when set.
    /// </summary>
    public bool? GlobalScopeOnly { get; set; }
}

public class FlagCatalogueListItemDto
{
    public int       flagId             { get; set; }
    public string    flagCode           { get; set; } = "";
    public string    flagName           { get; set; } = "";
    public string    flagDescription    { get; set; } = "";
    public int       flagTypeLkpId      { get; set; }
    public int?      severityLkpId      { get; set; }
    public decimal   defaultWeight      { get; set; }
    public bool      requiresEvidence   { get; set; }
    public string?   sourceSystem       { get; set; }
    public bool      activeFlag         { get; set; }
    public bool      isBuiltIn          { get; set; }
    public int       scopeCount         { get; set; }
    public DateTime  createdDate        { get; set; }
    public string?   lastUpdatedBy      { get; set; }
    public DateTime? lastUpdatedDate    { get; set; }

    /// <summary>
    /// Resolved scopes for the grid SCOPE chip(s). Empty for an unscoped
    /// flag. <c>productId == null</c> ⇒ all-products; <c>productName</c> /
    /// <c>tabName</c> are resolved server-side for display.
    /// </summary>
    public List<FlagScopeSummaryDto> scopes { get; set; } = new();
}

public class FlagScopeSummaryDto
{
    public int      flagScopeId  { get; set; }
    public int?     productId    { get; set; }   // null = all products
    public string?  productName  { get; set; }
    public int?     tabId        { get; set; }
    public string?  tabName      { get; set; }
    public bool     activeFlag   { get; set; }
}
