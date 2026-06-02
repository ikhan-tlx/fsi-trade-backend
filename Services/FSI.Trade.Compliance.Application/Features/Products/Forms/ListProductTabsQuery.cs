using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Slice 10.5 §DF-API §2 — lightweight tabs-only fetch for a product.
///
/// Drives the FE's first hop in the lazy-load flow:
/// <list type="number">
///   <item>User picks a product → FE calls this endpoint to render the
///         sub-product selector (or tab rail).</item>
///   <item>User picks a sub-product → FE calls
///         <c>GET /Product/{id}/Tabs/{tabId}/Fields</c> for the chosen
///         tab to populate the form on demand.</item>
/// </list>
///
/// KYC use-case: the FE wants every tab loaded upfront (no lazy pattern).
/// Either call this endpoint then fan out N <c>/Fields</c> calls in
/// parallel, or call the existing <c>/FormDefinition</c> for one-shot.
/// Both contracts are supported.
///
/// Cached server-side (15-min TTL) per (productId, culture). Same TTL as
/// FormDefinition; the underlying data is a subset of what FormDefinition
/// reads, so this cache and FormDefinition's cache can drift independently
/// without consistency risk.
/// </summary>
public record ListProductTabsQuery(int ProductId, string? Culture)
    : IRequest<ProductTabsDto>;

public class ProductTabsDto
{
    public int                productId     { get; set; }
    public string?            culture       { get; set; }

    /// <summary>
    /// Server-derived signal: the id of the tab that contains the
    /// sub-product selector checkboxes (fields with
    /// <c>FieldLookupType = "ProductTab"</c>), or <c>null</c> if the
    /// product has no selector.
    ///
    /// FE usage:
    /// <list type="bullet">
    ///   <item><b>Non-null</b> → product has sub-products (Import, Export,
    ///         Guarantee). FE fetches fields for <c>selectorTabId</c>
    ///         first to render the checkboxes, then lazy-loads the
    ///         destination tab on user selection (each checkbox's
    ///         <c>udfData</c> carries <c>{"TabId":N}</c>).</item>
    ///   <item><b>Null</b> → no selector tab (KYC and any other product
    ///         without sub-products). FE fetches all tab fields upfront
    ///         (either by calling <c>/FormDefinition</c> for one-shot
    ///         OR fanning out <c>/Tabs/{tabId}/Fields</c> in parallel).</item>
    /// </list>
    ///
    /// This removes the need for the FE to hardcode "KYC = load all,
    /// Trade = lazy" — the BE inspects the form schema and tells the
    /// FE directly.
    /// </summary>
    public int?               selectorTabId { get; set; }

    public List<FormTabDto>   tabs          { get; set; } = new();
}
