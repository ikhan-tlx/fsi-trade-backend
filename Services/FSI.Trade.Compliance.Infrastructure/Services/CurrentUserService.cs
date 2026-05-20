using System.Security.Claims;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace FSI.Trade.Compliance.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    public string? UserId          => _http.HttpContext?.User.FindFirstValue("userId")
                                       ?? _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName        => _http.HttpContext?.User.FindFirstValue("userName")
                                       ?? _http.HttpContext?.User.Identity?.Name;
    public bool    IsAuthenticated => _http.HttpContext?.User.Identity?.IsAuthenticated ?? false;
    public string? IpAddress       => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public int? TenantId
    {
        get
        {
            var raw = _http.HttpContext?.User.FindFirstValue("tenantId");
            return int.TryParse(raw, out var t) ? t : null;
        }
    }
}
