using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;
    public JwtTokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    public (string token, DateTime expiresAtUtc) IssueAccessToken(ApplicationUser user, IReadOnlyList<string> roles)
    {
        if (string.IsNullOrWhiteSpace(_opt.Key) || _opt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be set to at least 32 characters.");

        var ttl = ResolveTtl(roles);
        var now = DateTime.UtcNow;
        var exp = now.Add(ttl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,        user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? user.Id),
            new(JwtRegisteredClaimNames.Jti,        Guid.NewGuid().ToString("N")),
            new("userId",   user.Id),
            new("userName", user.UserName ?? string.Empty),
            new("tenantId", user.TenantId.ToString()),
        };
        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email!));
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer:             _opt.Issuer,
            audience:           _opt.Audience,
            claims:             claims,
            notBefore:          now,
            expires:            exp,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), exp);
    }

    private TimeSpan ResolveTtl(IReadOnlyList<string> roles)
    {
        int? minutes = null;
        foreach (var r in roles)
            if (_opt.RoleTtlMinutes.TryGetValue(r, out var m))
                minutes = (minutes is null) ? m : Math.Min(minutes.Value, m);

        if (minutes is null && _opt.RoleTtlMinutes.TryGetValue("DefaultExpiry", out var def))
            minutes = def;

        return TimeSpan.FromMinutes(minutes ?? _opt.AccessTokenDefaultMinutes);
    }
}
