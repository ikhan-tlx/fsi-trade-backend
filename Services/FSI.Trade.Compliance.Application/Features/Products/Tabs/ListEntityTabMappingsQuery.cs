using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.Tabs;

/// <summary>
/// Replaces legacy GET /api/v1/Entity/TabEntityMapping. Returns every active
/// row of TmX_Entity_Tab_Product_Mapping — small dataset (a few hundred rows
/// at most), heavily reused by the FE across every transaction page. Result
/// is cached server-side (5-min TTL).
/// </summary>
public record ListEntityTabMappingsQuery : IRequest<IReadOnlyList<EntityTabProductMappingDto>>;

public class EntityTabProductMappingDto
{
    public int   id              { get; set; }
    public int   tenantId        { get; set; }
    public int   entityId        { get; set; }
    public int   productId       { get; set; }
    public int   tabId           { get; set; }
    public int?  parentTabId     { get; set; }
    public int?  sortOrder       { get; set; }
}
