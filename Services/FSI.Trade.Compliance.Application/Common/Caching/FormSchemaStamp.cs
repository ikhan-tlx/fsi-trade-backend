using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Common.Caching;

/// <summary>
/// DB-derived companion to <see cref="FormSchemaCacheVersion"/>.
///
/// <para>The in-memory <see cref="FormSchemaCacheVersion"/> only changes when an
/// APP edit calls <c>Bump</c>. A raw-SQL change — a migration or a DBA script
/// that activates/deactivates a sub-product (<c>TmX_Entity_Tab_Product_Mapping.
/// Is_Active</c>) or edits a field (<c>TmX_Tenant_Field_Setup</c>) — leaves it
/// untouched, so the form-schema read caches (<c>FormDefinition</c>,
/// <c>TabFields</c>, <c>Tabs</c>) keep serving the PRE-change shape until the API
/// is restarted. That is exactly what made the Field Configuration preview differ
/// from the live product form after the <c>2026_08_066</c> deactivation
/// migration.</para>
///
/// <para>This stamp folds a cheap snapshot of the product's current field +
/// tab-mapping state into the cache key, so ANY change — including raw SQL —
/// forces a cache miss on the next read, with no restart or manual bump. It is a
/// composite of: the count of active field setups, the count of active tab
/// mappings, and the latest <c>Last_Updated</c>/<c>Created</c> tick across both
/// (so activation/deactivation is caught by the counts, and add/edit by the
/// tick). Two trivial indexed aggregates per read — negligible on these
/// admin/wizard endpoints.</para>
/// </summary>
public static class FormSchemaStamp
{
    public static async Task<string> ComputeAsync(
        IApplicationDbContext db, int productId, CancellationToken ct)
    {
        // Plain terminal aggregates (Count/Max) — reliably translated by EF Core,
        // unlike GroupBy over a constant key.
        var fields = db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId == productId && f.IsActive);
        var fieldCount = await fields.CountAsync(ct);
        DateTime? fieldMax = fieldCount == 0
            ? null
            : await fields.MaxAsync(f => (DateTime?)(f.LastUpdatedDate ?? f.CreatedDate), ct);

        var maps = db.EntityTabProductMappings.AsNoTracking()
            .Where(m => m.ProductId == productId && m.IsActive == 1);
        var mapCount = await maps.CountAsync(ct);
        DateTime? mapMax = mapCount == 0
            ? null
            : await maps.MaxAsync(m => (m.LastUpdatedDate ?? m.CreatedDate), ct);

        long latest = Math.Max(fieldMax?.Ticks ?? 0, mapMax?.Ticks ?? 0);
        return $"{mapCount}.{fieldCount}.{latest}";
    }
}
