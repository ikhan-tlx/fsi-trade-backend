using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;   // RiskBandClassifier / RiskBand
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Customer.List;

/// <summary>
/// Builds the customer listing by loading the tenant's customer snapshots,
/// grouping distinct by Customer_Code (latest snapshot wins for name/risk),
/// then filter → sort → page in memory. Mirrors the dashboard's customer
/// grouping; reuses <see cref="RiskBandClassifier"/> for the band.
/// </summary>
public class ListCustomersQueryHandler
    : IRequestHandler<ListCustomersQuery, PagedResult<CustomerListItemDto>>
{
    private const int PlatformDefaultTenantId = 1;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public ListCustomersQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db = db;
        _current = current;
    }

    public async Task<PagedResult<CustomerListItemDto>> Handle(ListCustomersQuery req, CancellationToken ct)
    {
        var tenantId = _current.TenantId ?? PlatformDefaultTenantId;

        var snaps = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CustomerCode != null)
            .Select(c => new { c.CustomerCode, c.CustomerName, c.CreatedDate, c.LastUpdatedDate, c.UdfData, c.TransactionId })
            .ToListAsync(ct);

        var all = snaps
            .GroupBy(s => s.CustomerCode!)
            .Select(grp =>
            {
                var latest = grp.OrderByDescending(x => x.CreatedDate).First();
                var risk   = ExtractRiskScore(latest.UdfData);
                var band   = RiskBandClassifier.Classify(risk);
                return new CustomerListItemDto
                {
                    customerCode     = grp.Key,
                    customerName     = latest.CustomerName,
                    riskScore        = risk,
                    riskBand         = BandLabel(band),
                    transactionCount = grp.Where(x => x.TransactionId.HasValue)
                                          .Select(x => x.TransactionId!.Value).Distinct().Count(),
                    onboardedDate    = grp.Min(x => x.CreatedDate),
                    lastActivityDate = grp.Max(x => x.LastUpdatedDate ?? x.CreatedDate)
                };
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            all = all.Where(c =>
                    c.customerCode.Contains(f, StringComparison.OrdinalIgnoreCase)
                 || (c.customerName != null && c.customerName.Contains(f, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        all = ApplySort(all, req.Sort);

        var total = all.Count;
        var items = all.Skip(req.Skip).Take(req.Take).ToList();

        return new PagedResult<CustomerListItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static List<CustomerListItemDto> ApplySort(List<CustomerListItemDto> list, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return list.OrderByDescending(c => c.lastActivityDate).ToList();

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
        if (first is null) return list.OrderByDescending(c => c.lastActivityDate).ToList();

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedEnumerable<CustomerListItemDto> ordered = field switch
        {
            "customername"     => desc ? list.OrderByDescending(c => c.customerName) : list.OrderBy(c => c.customerName),
            "customercode"     => desc ? list.OrderByDescending(c => c.customerCode) : list.OrderBy(c => c.customerCode),
            "riskband"         => desc ? list.OrderByDescending(c => c.riskBand)     : list.OrderBy(c => c.riskBand),
            "transactioncount" => desc ? list.OrderByDescending(c => c.transactionCount) : list.OrderBy(c => c.transactionCount),
            "onboardeddate"    => desc ? list.OrderByDescending(c => c.onboardedDate) : list.OrderBy(c => c.onboardedDate),
            _                  => desc ? list.OrderByDescending(c => c.lastActivityDate) : list.OrderBy(c => c.lastActivityDate),
        };
        return ordered.ToList();
    }

    internal static string BandLabel(RiskBand band) => band switch
    {
        RiskBand.Low    => "Low",
        RiskBand.Medium => "Medium",
        RiskBand.High   => "High",
        RiskBand.Pep    => "PEP",
        _               => "Unknown"
    };

    /// <summary>Reads RiskScore/RiskLevel from a snapshot UDF blob (key case/underscore tolerant).</summary>
    internal static string? ExtractRiskScore(string? udf)
    {
        if (string.IsNullOrWhiteSpace(udf)) return null;
        try
        {
            using var doc = JsonDocument.Parse(udf);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var name = p.Name.Replace("_", "");
                if (name.Equals("RiskScore", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("RiskLevel", StringComparison.OrdinalIgnoreCase))
                {
                    return p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        _                    => null
                    };
                }
            }
        }
        catch (JsonException) { /* corrupt UDF → no risk */ }
        return null;
    }
}
