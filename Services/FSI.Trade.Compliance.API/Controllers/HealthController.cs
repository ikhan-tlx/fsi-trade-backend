using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[AllowAnonymous]
[Route("api/v{version:apiVersion}/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    public HealthController(IApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            var count = await _db.Users.CountAsync(ct);
            var body  = new HealthResponse
            {
                Health        = "ok",
                DbReachable   = true,
                UserCount     = count,
                ServerTimeUtc = DateTime.UtcNow
            };
            return Ok(ResponseViewModel<HealthResponse>.Ok(body));
        }
        catch (Exception ex)
        {
            // Health is the one place we *want* a 503 (not a generic 500), and we
            // also want the body to follow our standard envelope so the FE's
            // monitoring tooling can parse it the same way as everything else.
            return StatusCode(503, new ResponseViewModel<object>
            {
                status = ResponseStatus.Error(503, "db_unreachable", ex.Message),
                data   = new { Success = 0, Code = "db_unreachable", Health = "down" }
            });
        }
    }

    public class HealthResponse
    {
        public string   Health        { get; set; } = "ok";
        public bool     DbReachable   { get; set; }
        public int      UserCount     { get; set; }
        public DateTime ServerTimeUtc { get; set; }
    }
}
