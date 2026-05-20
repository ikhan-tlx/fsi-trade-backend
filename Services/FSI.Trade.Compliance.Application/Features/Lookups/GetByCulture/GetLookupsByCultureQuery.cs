using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;

/// <summary>
/// Returns the full lookup catalog filtered to the caller's tenant and the
/// requested culture. The FE groups results client-side by <c>lookupType</c>
/// to populate dropdowns. No paging — the catalog is small (≤ a few thousand
/// rows in practice) and is loaded once at app-init.
/// </summary>
public record GetLookupsByCultureQuery(string Culture) : IRequest<List<LookupItemDto>>;
