using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Customer;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Customer.Common;   // CustomerInsightMetrics
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.CustomerInsight;

/// <summary>
/// Computes the book-wide portfolio KPIs (via the shared <see cref="CustomerInsightMetrics"/>)
/// and upserts a single row for today (UTC date) into TmX_Customer_Insight_Daily.
/// Idempotent: re-running on the same day refreshes the existing row.
/// </summary>
public class CustomerInsightSnapshotWriter : ICustomerInsightSnapshotWriter
{
    private readonly IApplicationDbContext _db;
    private readonly IOptionsMonitor<CustomerInsightSnapshotOptions> _opt;
    private readonly ILogger<CustomerInsightSnapshotWriter> _log;

    public CustomerInsightSnapshotWriter(
        IApplicationDbContext db,
        IOptionsMonitor<CustomerInsightSnapshotOptions> opt,
        ILogger<CustomerInsightSnapshotWriter> log)
    {
        _db = db;
        _opt = opt;
        _log = log;
    }

    public async Task<bool> WriteTodayAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        var m = await CustomerInsightMetrics.ComputeAsync(_db, now, ct);

        var row = await _db.CustomerInsightDailies
            .FirstOrDefaultAsync(s => s.SnapshotDate == today, ct);

        if (row is null)
        {
            _db.CustomerInsightDailies.Add(new CustomerInsightDaily
            {
                SnapshotDate   = today,
                TotalCustomers = m.TotalCustomers,
                HighRisk       = m.HighRisk,
                ReviewsOverdue = m.ReviewsOverdue,
                OpenCases      = m.OpenCases,
                FlagsRaised7d  = m.FlagsRaised7d,
                CreatedBy      = _opt.CurrentValue.WriterUserId,
                CreatedDate    = now,
            });
        }
        else
        {
            row.TotalCustomers  = m.TotalCustomers;
            row.HighRisk        = m.HighRisk;
            row.ReviewsOverdue  = m.ReviewsOverdue;
            row.OpenCases       = m.OpenCases;
            row.FlagsRaised7d   = m.FlagsRaised7d;
            row.LastUpdatedDate = now;
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation(
            "CustomerInsightSnapshot {Date:yyyy-MM-dd}: customers={C} high={H} overdue={O} openCases={K} flags7d={F}.",
            today, m.TotalCustomers, m.HighRisk, m.ReviewsOverdue, m.OpenCases, m.FlagsRaised7d);
        return true;
    }
}
