using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Configurations.GetForUser;

/// <summary>
/// Returns every configuration row for the caller's tenant. Read at app-init.
/// No paging — the configuration catalog is small per tenant.
/// </summary>
public record GetUserCompanyConfigurationsQuery : IRequest<List<ConfigurationItemDto>>;
