using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Common;

/// <summary>
/// Reconciles a branch's product entitlements in <c>TmX_Branch_Products_Mapping</c>
/// to the submitted set: wanted rows are inserted/(re)activated, rows no longer
/// wanted are deactivated (Active_Flag = 0). Called from the branch create/update
/// command handlers (which run on maker-checker approval), so the branch's product
/// LOV reflects the approved assignment. Deactivate-not-delete preserves history.
/// </summary>
public static class BranchProductSync
{
    public static async Task ApplyAsync(
        IApplicationDbContext db, int branchId, int tenantId,
        IEnumerable<BranchProductItemDto>? items, CancellationToken ct)
    {
        if (branchId <= 0) return;

        var wanted = (items ?? Enumerable.Empty<BranchProductItemDto>())
            .Select(i => i.Resolve())
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();

        var existing = await db.BranchProductMappings
            .Where(m => m.CompanyBranchId == branchId)
            .ToListAsync(ct);

        // Flip Active_Flag on existing rows to match the wanted set.
        var have = new HashSet<int>();
        foreach (var m in existing)
        {
            have.Add(m.ProductId);
            var active = wanted.Contains(m.ProductId);
            if (m.ActiveFlag != active) m.ActiveFlag = active;
        }

        // Insert rows for newly-wanted products.
        foreach (var pid in wanted)
        {
            if (have.Contains(pid)) continue;
            db.BranchProductMappings.Add(new BranchProductMapping
            {
                CompanyBranchId = branchId,
                ProductId       = pid,
                TenantId        = tenantId,
                ActiveFlag      = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
