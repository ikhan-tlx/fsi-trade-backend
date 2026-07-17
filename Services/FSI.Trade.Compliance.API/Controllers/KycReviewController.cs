using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Admin operations for Periodic Customer Review (all gated by Kyc.Review):
///   POST /api/v1/Kyc/Review/run                 — run the due sweep now (out-of-band).
///   POST /api/v1/Kyc/Review/run/{customerCode}  — force one customer's review now.
///   GET  /api/v1/Kyc/Review/schedule            — list schedules (ops monitoring).
/// The automatic schedule (PeriodicReviewService) runs regardless of these.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Kyc/Review")]
[Authorize]
public class KycReviewController : ControllerBase
{
    private readonly IPeriodicReviewRunner _runner;
    private readonly IApplicationDbContext _db;
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt;

    public KycReviewController(IPeriodicReviewRunner runner, IApplicationDbContext db, IOptionsMonitor<PeriodicReviewOptions> opt)
    { _runner = runner; _db = db; _opt = opt; }

    [HttpPost("run")]
    [RequiresPrivilege("Kyc.Review")]
    public async Task<IActionResult> RunDue(CancellationToken ct)
    {
        var o = _opt.CurrentValue;
        var n = await _runner.RunDueAsync(o.BatchSize, o.LeadTimeDays, ct);
        return Ok(ResponseViewModel<object>.Ok(new { processed = n }, $"Initiated {n} due review(s)."));
    }

    [HttpPost("run/{customerCode}")]
    [RequiresPrivilege("Kyc.Review")]
    public async Task<IActionResult> RunForCustomer(string customerCode, CancellationToken ct)
        => Ok(ResponseViewModel<ReviewRunOutcome>.Ok(await _runner.RunForCustomerAsync(customerCode, ct)));

    [HttpGet("schedule")]
    [RequiresPrivilege("Kyc.Review")]
    public async Task<IActionResult> GetSchedule([FromQuery] string? status, CancellationToken ct)
    {
        var q = _db.CustomerReviewSchedules.AsNoTracking().Where(s => s.TenantId == 1);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(s => s.Status == status);

        var rows = await q.OrderBy(s => s.NextReviewDate).Take(500)
            .Select(s => new
            {
                s.CustomerCode, s.CurrentRiskCategory, s.CurrentRiskScore,
                s.NextReviewDate, s.Status, s.LastReviewedDate, s.LastKycTransactionId, s.LastRemark
            })
            .ToListAsync(ct);

        return Ok(ResponseViewModel<object>.Ok(rows));
    }
}
