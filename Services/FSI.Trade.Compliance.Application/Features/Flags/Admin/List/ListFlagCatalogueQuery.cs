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
    /// <summary>Optional FLAG_CATEGORY lookup ID filter.</summary>
    public int?  CategoryLkpId   { get; set; }

    /// <summary>Optional FLAG_SEVERITY lookup ID filter.</summary>
    public int?  SeverityLkpId   { get; set; }

    /// <summary>Optional FLAG_TYPE lookup ID filter (Manual / Automated).</summary>
    public int?  FlagTypeLkpId   { get; set; }

    /// <summary>
    /// Null = both active and inactive. Mostly admins want active-only,
    /// but managing retired flags needs the off-switch.
    /// </summary>
    public bool? ActiveFlag      { get; set; }
}

public class FlagCatalogueListItemDto
{
    public int       flagId             { get; set; }
    public string    flagCode           { get; set; } = "";
    public string    flagName           { get; set; } = "";
    public string    flagDescription    { get; set; } = "";
    public int       flagTypeLkpId      { get; set; }
    public int?      flagCategoryLkpId  { get; set; }
    public int?      severityLkpId      { get; set; }
    public decimal   defaultWeight      { get; set; }
    public bool      requiresEvidence   { get; set; }
    public string?   sourceSystem       { get; set; }
    public bool      activeFlag         { get; set; }
    public int       scopeCount         { get; set; }
    public DateTime  createdDate        { get; set; }
    public string?   lastUpdatedBy      { get; set; }
    public DateTime? lastUpdatedDate    { get; set; }
}
