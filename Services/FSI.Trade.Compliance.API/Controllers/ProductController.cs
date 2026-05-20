using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Products.ActiveLov;
using FSI.Trade.Compliance.Application.Features.Products.Forms;
using FSI.Trade.Compliance.Application.Features.Products.List;
using FSI.Trade.Compliance.Application.Features.Products.Tabs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Product-configuration surface — replaces two legacy endpoints in one
/// domain-named controller:
///
///   GET    /api/v1/Product/Tabs/Mappings                        — every active entity-tab-product mapping (cached 5min)
///   GET    /api/v1/Product/{id}/FormDefinition?culture=...      — nested tabs+fields tree for one product (cached 15min)
///
/// Both endpoints are read-mostly, idempotent, and shared across every
/// dynamic-form page the FE renders.
///
/// Legacy mapping:
/// - GET /api/v1/Entity/TabEntityMapping                            → /Product/Tabs/Mappings
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

    /// <summary>Every active entity-tab-product mapping. Server-cached 5 min.</summary>
    [HttpGet("Tabs/Mappings")]
    public async Task<IActionResult> TabMappings(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListEntityTabMappingsQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<EntityTabProductMappingDto>>.Ok(rows));
    }

    /// <summary>Tabs + fields tree for one product. Server-cached 15 min per (productId, culture).</summary>
    [HttpGet("{productId:int}/FormDefinition")]
    public async Task<IActionResult> FormDefinition(int productId, [FromQuery] string? culture, CancellationToken ct)
    {
        var def = await _mediator.Send(new GetProductFormDefinitionQuery(productId, culture), ct);
        return Ok(ResponseViewModel<ProductFormDefinitionDto>.Ok(def));
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
