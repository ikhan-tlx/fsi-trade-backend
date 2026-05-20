using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

public interface IJwtTokenService
{
    (string token, DateTime expiresAtUtc) IssueAccessToken(ApplicationUser user, IReadOnlyList<string> roles);
}
