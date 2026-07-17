using System.Security.Claims;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace FSI.Trade.Compliance.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;
    private readonly IAmbientUserContext  _ambient;

    public CurrentUserService(IHttpContextAccessor http, IAmbientUserContext ambient)
    {
        _http    = http;
        _ambient = ambient;
    }

    // Ambient (in-process impersonation) wins when set; else the HTTP principal.
    public string? UserId          => _ambient.Current?.UserId
                                       ?? _http.HttpContext?.User.FindFirstValue("userId")
                                       ?? _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName        => _ambient.Current?.UserName
                                       ?? _http.HttpContext?.User.FindFirstValue("userName")
                                       ?? _http.HttpContext?.User.Identity?.Name;
    public bool    IsAuthenticated => _ambient.Current is not null
                                       || (_http.HttpContext?.User.Identity?.IsAuthenticated ?? false);
    public string? IpAddress       => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public int? TenantId
    {
        get
        {
            if (_ambient.Current is not null) return _ambient.Current.TenantId;
            var raw = _http.HttpContext?.User.FindFirstValue("tenantId");
            return int.TryParse(raw, out var t) ? t : null;
        }
    }
}
