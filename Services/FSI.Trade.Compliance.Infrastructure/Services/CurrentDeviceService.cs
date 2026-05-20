using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Services;

public class CurrentDeviceService : ICurrentDeviceService
{
    private readonly IHttpContextAccessor _http;
    private readonly AuthOptions          _opt;

    public CurrentDeviceService(IHttpContextAccessor http, IOptions<AuthOptions> opt)
    {
        _http = http;
        _opt  = opt.Value;
    }

    public string? DeviceId
    {
        get
        {
            var headers = _http.HttpContext?.Request.Headers;
            if (headers is null) return null;
            return headers.TryGetValue(_opt.DeviceIdHeaderName, out var values)
                ? values.ToString()
                : null;
        }
    }

    public string? UserAgent =>
        _http.HttpContext?.Request.Headers.UserAgent.ToString();
}
