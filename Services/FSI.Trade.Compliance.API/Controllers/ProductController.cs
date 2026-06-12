using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Products.ActiveLov;
using FSI.Trade.Compliance.Application.Features.Products.Create;
using FSI.Trade.Compliance.Application.Features.Products.Forms;
using FSI.Trade.Compliance.Application.Features.Products.GetById;
using FSI.Trade.Compliance.Application.Features.Products.List;
using FSI.Trade.Compliance.Application.Features.Products.SetActive;
using FSI.Trade.Compliance.Application.Features.Products.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Product-configuration surface. Endpoint inventory (all read-mostly,
/// cached server-side):
///
///   GET /api/v1/Product/list                                     — active products
///   GET /api/v1/Product/ActiveLov                                — active products in legacy LOV shape
///   GET /api/v1/Product/{id}/Tabs?culture=...                    — tabs only (lightweight); carries selectorTabId signal
///   GET /api/v1/Product/{id}/Tabs/{tabId}/Fields?culture=...     — fields for one tab (lazy-load)
///   GET /api/v1/Product/{id}/FormDefinition?culture=...          — full tabs+fields tree (one-shot, e.g. KYC)
///
/// Legacy URL mapping (decommissioned):
/// - GET /api/v1/Entity/TabEntityMapping                            → use /Product/{id}/Tabs (per-product, scoped)
/// - GET /api/v1/Product/Tabs/Mappings                              → use /Product/{id}/Tabs (removed 2026-05-25)
/// - GET /api/v1/TenantFieldSetup/GetFieldsByProduct/{id}/{culture} → /Product/{id}/FormDefinition?culture=
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ProductController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProductController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Paged admin grid of products (server-side sort + filter), ALL products
    /// incl. inactive. Backs the FE "Products" management page. Slice 14.
    ///
    ///   GET /api/v1/Product?page=1&amp;pageSize=10&amp;sort=ProductName-asc&amp;filter=loan
    ///
    /// The active-only flat read stays on <c>/list</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListPaged([FromQuery] ListProductsPagedQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<ProductPagedItemDto>>.Ok(paged));
    }

    /// <summary>Single product by id (edit form). Slice 14.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetProductByIdQuery(id), ct);
        return Ok(ResponseViewModel<ProductPagedItemDto>.Ok(dto));
    }

    /// <summary>Create a product. Slice 14.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand cmd, CancellationToken ct)
    {
        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { productId = id }, "Product created"));
    }

    /// <summary>Update a product. Slice 14.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductCommand body, CancellationToken ct)
    {
        body.ProductId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { productId = id }, "Product updated"));
    }

    /// <summary>Activate a product. Slice 14.</summary>
    [HttpPut("Activate/{id:int}")]
    public async Task<IActionResult> Activate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetProductActiveCommand(id, true), ct);
        return Ok(ResponseViewModel<object>.Ok(new { productId = id, isActive = true }, "Product activated"));
    }

    /// <summary>Deactivate a product. Slice 14.</summary>
    [HttpPut("Deactivate/{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetProductActiveCommand(id, false), ct);
        return Ok(ResponseViewModel<object>.Ok(new { productId = id, isActive = false }, "Product deactivated"));
    }

    /// <summary>
    /// Tabs + fields tree for one product (full preload). Server-cached
    /// 15 min per (productId, culture).
    ///
    /// Use this when the caller needs every field for the product in one
    /// shot — e.g. KYC, where all tabs are loaded together. For the
    /// lazy-load wizard flow, prefer
    /// <c>GET /Product/{id}/Tabs</c> + <c>GET /Product/{id}/Tabs/{tabId}/Fields</c>.
    /// </summary>
    [HttpGet("{productId:int}/FormDefinition")]
    public async Task<IActionResult> FormDefinition(int productId, [FromQuery] string? culture, CancellationToken ct)
    {
        var def = await _mediator.Send(new GetProductFormDefinitionQuery(productId, culture), ct);
        return Ok(ResponseViewModel<ProductFormDefinitionDto>.Ok(def));
    }

    /// <summary>
    /// Tabs-only tree for one product (lightweight). No fields included —
    /// each tab's <c>fields</c> array is empty. Designed for the FE's
    /// lazy-load flow: render tab rail / sub-product selector first, then
    /// fetch fields for the active tab via
    /// <c>GET /Product/{id}/Tabs/{tabId}/Fields</c> on demand.
    ///
    /// Server-cached 15 min per (productId, culture).
    /// </summary>
    [HttpGet("{productId:int}/Tabs")]
    public async Task<IActionResult> Tabs(int productId, [FromQuery] string? culture, CancellationToken ct)
    {
        var tabs = await _mediator.Send(new ListProductTabsQuery(productId, culture), ct);
        return Ok(ResponseViewModel<ProductTabsDto>.Ok(tabs));
    }

    /// <summary>
    /// Fields for one specific tab inside a product. Validates the
    /// (productId, tabId) mapping before returning data — 404 if the tab
    /// isn't attached to the product. Same filters as
    /// <c>FormDefinition</c> (Is_Active + MRL belt-and-braces).
    ///
    /// Server-cached 15 min per (productId, tabId, culture). Finer-grained
    /// than the FormDefinition cache, so a field edit invalidates only
    /// that tab's entry.
    /// </summary>
    [HttpGet("{productId:int}/Tabs/{tabId:int}/Fields")]
    public async Task<IActionResult> TabFields(int productId, int tabId, [FromQuery] string? culture, CancellationToken ct)
    {
        var fields = await _mediator.Send(new GetTabFieldsQuery(productId, tabId, culture), ct);
        return Ok(ResponseViewModel<TabFieldsDto>.Ok(fields));
    }

    /// <summary>
    /// Slice 6.5 — active products list for FE dropdowns and the new wizard
    /// product-selection step. Replaces legacy <c>GET /api/v1/Product/list</c>.
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListProductsQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<ProductListItemDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 7 — active products in the legacy LOV shape (LookupId /
    /// VisibleValue / HiddenValue / Description) for the Reports filter
    /// panel. Mirrors the legacy <c>GET /api/v1/Product/ActiveLov</c>.
    /// </summary>
    [HttpGet("ActiveLov")]
    public async Task<IActionResult> ActiveLov(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListActiveProductsLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<ProductLovItemDto>>.Ok(rows));
    }
}
