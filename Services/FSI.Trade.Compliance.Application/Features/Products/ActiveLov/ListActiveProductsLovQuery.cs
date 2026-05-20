using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.ActiveLov;

/// <summary>
/// Slice 7 (Reports) — active-products LOV for the reports filter panel.
/// Mirrors the legacy <c>GET /api/v1/Product/ActiveLov</c> endpoint.
///
/// Returns the same population as <see cref="List.ListProductsQuery"/>
/// (active + effective-date-windowed), but in the legacy LOV shape the
/// FE businessReports api expects — <c>{ lookupId, visibleValue }</c> —
/// so the existing FE mapper round-trips with minimal changes.
/// </summary>
public record ListActiveProductsLovQuery : IRequest<IReadOnlyList<ProductLovItemDto>>;

public class ProductLovItemDto
{
    public int    lookupId     { get; set; }
    public string visibleValue { get; set; } = "";
    public string hiddenValue  { get; set; } = "";
    public string? description { get; set; }
}
