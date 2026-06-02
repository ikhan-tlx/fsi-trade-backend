using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Slice 10.5 §DF-API §2 — fields for one specific tab inside a product's
/// dynamic form. Second hop in the lazy-load flow (see
/// <see cref="ListProductTabsQuery"/> for the first).
///
/// Why scoped to (product, tab)
/// ----------------------------
/// Security boundary: the handler verifies the tab actually belongs to the
/// product via <c>TmX_Entity_Tab_Product_Mapping</c> before returning any
/// rows. A caller can't read fields for a tab attached to a product they
/// didn't ask for.
///
/// Cached server-side (15-min TTL) per (productId, tabId, culture).
/// Per-tab caching means a field edit to one tab invalidates only that
/// tab's entry — finer granularity than the FormDefinition cache.
/// </summary>
public record GetTabFieldsQuery(int ProductId, int TabId, string? Culture)
    : IRequest<TabFieldsDto>;

public class TabFieldsDto
{
    public int                  productId  { get; set; }
    public int                  tabId      { get; set; }
    public string?              culture    { get; set; }
    public string?              tabName    { get; set; }
    public string?              localeLabel{ get; set; }
    public List<FormFieldDto>   fields     { get; set; } = new();
}
