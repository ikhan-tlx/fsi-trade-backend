using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

/// <summary>
/// "Deploy this flag to a (product, tab) combo." The flag must already
/// exist in the catalogue; this just creates the scope row that lets
/// it appear on the form for that product/tab.
///
/// Maps to <c>POST /api/v1/Flag/{id}/Scopes</c>. Gated by <c>Flags.Manage</c>.
/// </summary>
public class AddFlagScopeCommand : IRequest<int>
{
    // Path-bound — set by the controller from {id}; not part of the body.
    public int   flagId      { get; set; }

    public int   productId   { get; set; }
    public int?  tabId       { get; set; }     // null = product-level (e.g. KYC)
    public int?  sortOrder   { get; set; }     // defaults to 0
    public bool? activeFlag  { get; set; }     // defaults to true
}
