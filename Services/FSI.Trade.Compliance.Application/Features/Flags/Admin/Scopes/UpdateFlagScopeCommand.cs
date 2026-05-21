using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

/// <summary>
/// Update an existing scope row. Today supports toggling
/// <see cref="activeFlag"/> (the legacy "Visibility = '0'" use case)
/// and re-ordering via <see cref="sortOrder"/>. Product / Tab moves
/// require delete + add (those identify the row).
///
/// Maps to <c>PUT /api/v1/Flag/Scope/{scopeId}</c>. Gated by
/// <c>Flags.Manage</c>.
/// </summary>
public class UpdateFlagScopeCommand : IRequest<Unit>
{
    // Path-bound.
    public int  flagScopeId { get; set; }

    public bool? activeFlag { get; set; }
    public int?  sortOrder  { get; set; }
}
