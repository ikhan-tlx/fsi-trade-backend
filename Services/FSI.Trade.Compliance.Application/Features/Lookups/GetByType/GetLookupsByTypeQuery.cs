using FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByType;

/// <summary>
/// Slice 6.5 — single-type lookup for FE dropdowns. Replaces legacy
/// <c>GET /api/v1/Lookup/GetByType?type=X</c>.
///
/// Sibling to <see cref="GetByCulture.GetLookupsByCultureQuery"/> which
/// returns the WHOLE catalog. This is the narrower form — handy when the
/// FE only needs one type (ProductTypes, Currency, etc.) and doesn't want
/// to pull + filter the full set client-side.
///
/// Reuses <see cref="LookupItemDto"/> so the shape is identical to the
/// per-row entries in the by-culture endpoint.
/// </summary>
public record GetLookupsByTypeQuery(string Type, string? Culture)
    : IRequest<IReadOnlyList<LookupItemDto>>;
