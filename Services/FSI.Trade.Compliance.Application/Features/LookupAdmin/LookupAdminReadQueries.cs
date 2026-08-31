using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.LookupAdmin;

// ═══════════════ Shared usage helpers ═══════════════════════════════════════
internal static class LookupUsage
{
    /// <summary>Split a rule's StaticValue (possibly a delimited "In" list) into trimmed tokens.</summary>
    public static IEnumerable<string> Tokens(string? staticValue)
    {
        if (string.IsNullOrWhiteSpace(staticValue)) yield break;
        foreach (var t in staticValue.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var s = t.Trim();
            if (s.Length > 0) yield return s;
        }
    }


    public static string Humanize(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return code ?? "";
        var s = code.Replace('_', ' ').Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[A-Z])(?=[A-Z][a-z])", " ");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length > 1 && w.All(char.IsUpper)) continue;   // keep acronyms as-is
            words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
        }
        return string.Join(' ', words);
    }
}

// ═══════════════ Types list ══════════════════════════════════════════════════
/// <summary>GET /api/v1/LookupAdmin/Types — managed types (allow-list ∩ referenced-by-field).</summary>
public class ListManagedTypesQuery : PagedQuery, IRequest<PagedResult<ManagedLookupTypeDto>>
{
    public string? Status        { get; set; }   // active | inactive | all
    public bool?   Hierarchical  { get; set; }
}

public class ListManagedTypesQueryHandler : IRequestHandler<ListManagedTypesQuery, PagedResult<ManagedLookupTypeDto>>
{
    private readonly IApplicationDbContext _db;
    public ListManagedTypesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<ManagedLookupTypeDto>> Handle(ListManagedTypesQuery req, CancellationToken ct)
    {
        // Distinct field bindings by lookup type (only allow-listed types survive).
        var bindings = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.FieldLookupType != null)
            .Select(f => new { f.FieldLookupType, f.TenantFieldSetupId })
            .ToListAsync(ct);

        var boundCountByType = bindings
            .Where(b => LookupAdminAllowList.Contains(b.FieldLookupType))
            .GroupBy(b => b.FieldLookupType!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TenantFieldSetupId).Distinct().Count(), StringComparer.OrdinalIgnoreCase);

        // Managed set = the compiled allow-list UNION the admin-created types recorded in
        // the TmX_Lookup_Type registry (Is_Manageable = 1). (boundFieldCount below is still
        // computed from field bindings, for information only; 0 = not bound to any field.)
        var registryTypes = await _db.LookupTypeRegistrations.AsNoTracking()
            .Where(r => r.IsManageable)
            .Select(r => r.LookupType)
            .ToListAsync(ct);

        var managedTypes = LookupAdminAllowList.Types
            .Concat(registryTypes)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (managedTypes.Count == 0)
            return new PagedResult<ManagedLookupTypeDto> { items = new(), total = 0, page = req.Page, pageSize = req.PageSize };

        // Value stats for the managed types (single grouped pull).
        var lookupRows = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType != null && managedTypes.Contains(l.LookupType))
            .Select(l => new { l.LookupType, l.IsActive, l.ParentLookupId, l.Description })
            .ToListAsync(ct);

        var statsByType = lookupRows
            .GroupBy(l => l.LookupType!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new
            {
                total  = g.Count(),
                active = g.Count(x => x.IsActive),
                hier   = g.Any(x => x.ParentLookupId != null),
            }, StringComparer.OrdinalIgnoreCase);

        var rows = managedTypes.Select(t =>
        {
            statsByType.TryGetValue(t, out var s);
            var activeCount = s?.active ?? 0;
            return new ManagedLookupTypeDto
            {
                lookupType       = t,
                displayName      = LookupUsage.Humanize(t),
                description      = null,
                boundFieldCount  = boundCountByType.TryGetValue(t, out var bc) ? bc : 0,
                valueCount       = s?.total ?? 0,
                activeValueCount = activeCount,
                isHierarchical   = s?.hier ?? false,
                editable         = true,
                source           = "MANAGED",
                // No independent type-lifecycle column exists; a managed type reads as
                // Active while it has any live value, else Inactive (governance view).
                status           = activeCount > 0 ? "Active" : "Inactive",
            };
        }).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim().ToLowerInvariant();
            rows = rows.Where(r => (r.lookupType + " " + r.displayName).ToLowerInvariant().Contains(f));
        }
        if (!string.IsNullOrWhiteSpace(req.Status) && !string.Equals(req.Status, "all", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r => string.Equals(r.status, req.Status, StringComparison.OrdinalIgnoreCase));
        if (req.Hierarchical is { } h)
            rows = rows.Where(r => r.isHierarchical == h);

        rows = SortTypes(rows, req.Sort);
        var all = rows.ToList();
        return new PagedResult<ManagedLookupTypeDto>
        {
            items    = all.Skip(req.Skip).Take(req.Take).ToList(),
            total    = all.Count,
            page     = req.Page,
            pageSize = req.PageSize,
        };
    }

    private static IEnumerable<ManagedLookupTypeDto> SortTypes(IEnumerable<ManagedLookupTypeDto> q, string? sort)
    {
        var (field, desc) = ParseSort(sort, "displayname");
        Func<ManagedLookupTypeDto, object> key = field switch
        {
            "type"       => r => r.lookupType,
            "boundfields"=> r => r.boundFieldCount,
            "values"     => r => r.valueCount,
            "active"     => r => r.activeValueCount,
            "status"     => r => r.status,
            _            => r => r.displayName,
        };
        return desc ? q.OrderByDescending(key) : q.OrderBy(key);
    }

    internal static (string field, bool desc) ParseSort(string? sort, string def)
    {
        if (string.IsNullOrWhiteSpace(sort)) return (def, false);
        var s = sort.Split(',')[0].Trim();
        var dash = s.LastIndexOf('-');
        if (dash <= 0) return (s.ToLowerInvariant(), false);
        return (s[..dash].Trim().ToLowerInvariant(), s[(dash + 1)..].Trim().Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}

// ═══════════════ Type usage (where used) ════════════════════════════════════
/// <summary>GET /api/v1/LookupAdmin/Types/{lookupType}/Usage — bound fields (product/tab/label).</summary>
public record GetLookupTypeUsageQuery(string LookupType) : IRequest<List<LookupTypeUsageDto>>;

public class GetLookupTypeUsageQueryHandler : IRequestHandler<GetLookupTypeUsageQuery, List<LookupTypeUsageDto>>
{
    private readonly IApplicationDbContext _db;
    public GetLookupTypeUsageQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<LookupTypeUsageDto>> Handle(GetLookupTypeUsageQuery req, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(_db, req.LookupType, ct);
        var lt = req.LookupType.Trim();

        var fields = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.FieldLookupType == lt)
            .Select(f => new { f.ProductId, f.TabId, f.FieldLabel, f.FieldName })
            .ToListAsync(ct);
        if (fields.Count == 0) return new();

        var productIds = fields.Where(f => f.ProductId != null).Select(f => f.ProductId!.Value).Distinct().ToList();
        var tabIds     = fields.Where(f => f.TabId != null).Select(f => f.TabId!.Value).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.ProductCode, p.ProductName }).ToListAsync(ct);
        var productById = products.ToDictionary(p => p.ProductId);

        var tabs = await _db.Tabs.AsNoTracking()
            .Where(t => tabIds.Contains(t.TabId))
            .Select(t => new { t.TabId, t.TabName, t.LocaleLabel }).ToListAsync(ct);
        var tabById = tabs.ToDictionary(t => t.TabId);

        return fields.Select(f =>
        {
            string? productCode = null;
            if (f.ProductId != null && productById.TryGetValue(f.ProductId.Value, out var p))
                productCode = p.ProductCode ?? p.ProductName;
            string? tabTitle = null;
            if (f.TabId != null && tabById.TryGetValue(f.TabId.Value, out var tb))
                tabTitle = tb.LocaleLabel ?? tb.TabName;
            return new LookupTypeUsageDto
            {
                productCode = productCode,
                tabTitle    = tabTitle,
                fieldLabel  = f.FieldLabel ?? (f.FieldName != null ? LookupUsage.Humanize(f.FieldName) : null),
            };
        })
        .OrderBy(u => u.productCode).ThenBy(u => u.tabTitle).ThenBy(u => u.fieldLabel)
        .ToList();
    }
}

// ═══════════════ Values list ═════════════════════════════════════════════════
/// <summary>GET /api/v1/LookupAdmin/Values?lookupType=&amp;q=&amp;active=&amp;parentId=&amp;page=&amp;pageSize=&amp;sort=</summary>
public class ListLookupValuesQuery : PagedQuery, IRequest<PagedResult<LookupValueDto>>
{
    public string? LookupType { get; set; }
    public string? Q          { get; set; }
    public bool?   Active     { get; set; }
    public int?    ParentId   { get; set; }
}

public class ListLookupValuesQueryHandler : IRequestHandler<ListLookupValuesQuery, PagedResult<LookupValueDto>>
{
    private readonly IApplicationDbContext _db;
    public ListLookupValuesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<LookupValueDto>> Handle(ListLookupValuesQuery req, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(_db, req.LookupType, ct);
        var lt = req.LookupType!.Trim();

        var rowsRaw = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == lt)
            .Select(l => new
            {
                l.Id, l.LookupType, l.VisibleValue, l.HiddenValue, l.Description,
                l.ParentLookupId, l.SortOrder, l.IsActive, l.UserEditable, l.LastUpdatedBy,
            })
            .ToListAsync(ct);

        // Parent labels (resolve any referenced parent id → Visible_Value).
        var parentIds = rowsRaw.Where(r => r.ParentLookupId != null).Select(r => r.ParentLookupId!.Value).Distinct().ToList();
        var parentLabels = parentIds.Count == 0
            ? new Dictionary<int, string?>()
            : (await _db.Lookups.AsNoTracking()
                    .Where(l => parentIds.Contains(l.Id))
                    .Select(l => new { l.Id, l.VisibleValue }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.VisibleValue);

        // Rule usage: active FlagRules whose LEFT field is bound to this type and
        // whose StaticValue references the value's code (Hidden_Value). Computed once.
        var boundFieldNames = (await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.FieldLookupType == lt && f.FieldName != null)
                .Select(f => f.FieldName!).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ruleCountByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (boundFieldNames.Count > 0)
        {
            var rules = await _db.FlagRules.AsNoTracking()
                .Where(r => r.ActiveFlag && r.StaticValue != null && r.LeftFieldCode != null)
                .Select(r => new { r.LeftFieldCode, r.StaticValue }).ToListAsync(ct);
            foreach (var rule in rules.Where(r => boundFieldNames.Contains(r.LeftFieldCode!)))
                foreach (var tok in LookupUsage.Tokens(rule.StaticValue))
                    ruleCountByCode[tok] = ruleCountByCode.TryGetValue(tok, out var c) ? c + 1 : 1;
        }

        var rows = rowsRaw.Select(r =>
        {
            var ruleUsage = r.HiddenValue != null && ruleCountByCode.TryGetValue(r.HiddenValue, out var rc) ? rc : 0;
            var editable  = r.UserEditable ?? true;   // NULL (legacy/unset) = editable; only an explicit 0 locks
            return new LookupValueDto
            {
                id              = r.Id,
                lookupType      = r.LookupType,
                label           = r.VisibleValue,
                code            = r.HiddenValue,
                description     = r.Description,
                parentId        = r.ParentLookupId,
                parentLabel     = r.ParentLookupId != null && parentLabels.TryGetValue(r.ParentLookupId.Value, out var pl) ? pl : null,
                sortOrder       = r.SortOrder,
                isActive        = r.IsActive,
                lastUpdatedBy   = r.LastUpdatedBy,
                lastUpdatedDate = null,   // Created/LastUpdated dates are unmapped on Lookup (legacy nvarchar) — see LookupConfiguration.
                // Per-row transaction scanning is deferred to GET /Values/{id}/Usage (see LookupValueUsage) to keep the list fast.
                txnUsageCount   = 0,
                ruleUsageCount  = ruleUsage,
                editable        = editable,
                deletable       = editable && ruleUsage == 0,
            };
        }).AsEnumerable();

        // Filters.
        var q = req.Q ?? req.Filter;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var f = q.Trim().ToLowerInvariant();
            rows = rows.Where(r => ((r.label ?? "") + " " + (r.code ?? "") + " " + (r.description ?? "")).ToLowerInvariant().Contains(f));
        }
        if (req.Active is { } act)
            rows = rows.Where(r => r.isActive == act);
        if (req.ParentId is { } pid)
            rows = rows.Where(r => r.parentId == pid);

        // Sort (default: sortOrder asc, then label).
        var (field, desc) = ListManagedTypesQueryHandler.ParseSort(req.Sort, "sortorder");
        Func<LookupValueDto, object> key = field switch
        {
            "label"       => r => r.label ?? "",
            "code"        => r => r.code ?? "",
            "status"      => r => r.isActive,
            "lastupdated" => r => r.lastUpdatedBy ?? "",
            _             => r => r.sortOrder ?? int.MaxValue,
        };
        rows = desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
        if (field == "sortorder") rows = ((IOrderedEnumerable<LookupValueDto>)rows).ThenBy(r => r.label);

        var allRows = rows.ToList();
        return new PagedResult<LookupValueDto>
        {
            items    = allRows.Skip(req.Skip).Take(req.Take).ToList(),
            total    = allRows.Count,
            page     = req.Page,
            pageSize = req.PageSize,
        };
    }
}

// ═══════════════ Parent options (hierarchical) ══════════════════════════════
/// <summary>GET /api/v1/Lookup/ParentOptions?lookupType= — candidate parents for a hierarchical
/// type. A value's parent may live in a DIFFERENT lookup type (e.g. HSCodeList values are children
/// of LOB values), so this resolves the parent type from the type's existing children and returns
/// ALL values of that parent type. Empty when the type has no established parent relationship.</summary>
public record ListLookupParentOptionsQuery(string LookupType) : IRequest<List<LookupParentOptionDto>>;

public class LookupParentOptionDto
{
    public int     id         { get; set; }
    public string? label      { get; set; }
    public string? code       { get; set; }
    public string? parentType { get; set; }
}

public class ListLookupParentOptionsQueryHandler
    : IRequestHandler<ListLookupParentOptionsQuery, List<LookupParentOptionDto>>
{
    private readonly IApplicationDbContext _db;
    public ListLookupParentOptionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<LookupParentOptionDto>> Handle(ListLookupParentOptionsQuery req, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(_db, req.LookupType, ct);
        var lt = req.LookupType!.Trim();

        // The parent ids this type's values actually reference.
        var parentIds = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == lt && l.ParentLookupId != null)
            .Select(l => l.ParentLookupId!.Value).Distinct().ToListAsync(ct);
        if (parentIds.Count == 0) return new();   // no established parent relationship

        // Resolve the parent lookup type(s) — usually one foreign type (e.g. LOB for HSCodeList).
        var parentTypes = await _db.Lookups.AsNoTracking()
            .Where(l => parentIds.Contains(l.Id) && l.LookupType != null)
            .Select(l => l.LookupType!).Distinct().ToListAsync(ct);
        if (parentTypes.Count == 0) return new();

        // Every value of the parent type is a candidate parent (so a child can be re-pointed).
        // Projection only (never a full-entity Lookup load — legacy NULL columns throw).
        var opts = await _db.Lookups.AsNoTracking()
            .Where(l => parentTypes.Contains(l.LookupType!))
            .Select(l => new LookupParentOptionDto
            {
                id = l.Id, label = l.VisibleValue, code = l.HiddenValue, parentType = l.LookupType,
            })
            .ToListAsync(ct);

        return opts.OrderBy(o => o.label, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

// ═══════════════ Value detail ═══════════════════════════════════════════════
public record GetLookupValueByIdQuery(int Id) : IRequest<LookupValueDto?>;

public class GetLookupValueByIdQueryHandler : IRequestHandler<GetLookupValueByIdQuery, LookupValueDto?>
{
    private readonly IApplicationDbContext _db;
    public GetLookupValueByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<LookupValueDto?> Handle(GetLookupValueByIdQuery req, CancellationToken ct)
    {
        var l = await _db.Lookups.AsNoTracking()
            .Where(x => x.Id == req.Id)
            .Select(x => new
            {
                x.Id, x.LookupType, x.VisibleValue, x.HiddenValue, x.Description,
                x.ParentLookupId, x.SortOrder, x.IsActive, x.UserEditable, x.LastUpdatedBy,
            })
            .FirstOrDefaultAsync(ct);
        if (l is null) return null;

        // A value only surfaces here if its type is manageable.
        await LookupAdminGuard.EnsureManageableAsync(_db, l.LookupType, ct);

        string? parentLabel = null;
        if (l.ParentLookupId != null)
            parentLabel = await _db.Lookups.AsNoTracking()
                .Where(x => x.Id == l.ParentLookupId.Value).Select(x => x.VisibleValue).FirstOrDefaultAsync(ct);

        var usage = await LookupValueUsageComputer.ComputeAsync(_db, l.LookupType!, l.HiddenValue, ct);
        var editable = l.UserEditable ?? true;   // NULL (legacy/unset) = editable; only an explicit 0 locks

        return new LookupValueDto
        {
            id              = l.Id,
            lookupType      = l.LookupType,
            label           = l.VisibleValue,
            code            = l.HiddenValue,
            description     = l.Description,
            parentId        = l.ParentLookupId,
            parentLabel     = parentLabel,
            sortOrder       = l.SortOrder,
            isActive        = l.IsActive,
            lastUpdatedBy   = l.LastUpdatedBy,
            lastUpdatedDate = null,
            txnUsageCount   = usage.txnUsageCount,
            ruleUsageCount  = usage.ruleUsageCount,
            editable        = editable,
            deletable       = editable && !usage.inUse,
        };
    }
}

// ═══════════════ Value usage (in-use detail) ════════════════════════════════
public record GetLookupValueUsageQuery(int Id) : IRequest<LookupValueUsageDto>;

public class GetLookupValueUsageQueryHandler : IRequestHandler<GetLookupValueUsageQuery, LookupValueUsageDto>
{
    private readonly IApplicationDbContext _db;
    public GetLookupValueUsageQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<LookupValueUsageDto> Handle(GetLookupValueUsageQuery req, CancellationToken ct)
    {
        var l = await _db.Lookups.AsNoTracking()
            .Where(x => x.Id == req.Id)
            .Select(x => new { x.Id, x.LookupType, x.HiddenValue })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");

        await LookupAdminGuard.EnsureManageableAsync(_db, l.LookupType, ct);
        return await LookupValueUsageComputer.ComputeAsync(_db, l.LookupType!, l.HiddenValue, ct);
    }
}

/// <summary>
/// Best-effort in-use detection for a single value (plan §4.6). Rule usage is
/// precise (active FlagRules on a field bound to the type, referencing the
/// code); transaction usage is a best-effort substring scan of the
/// <c>TmX_Transaction_Detail</c> UDF JSON blob. Only invoked on the single-value
/// paths (detail / usage / delete) — never per list row.
/// </summary>
internal static class LookupValueUsageComputer
{
    public static async Task<LookupValueUsageDto> ComputeAsync(
        IApplicationDbContext db, string lookupType, string? code, CancellationToken ct)
    {
        var result = new LookupValueUsageDto();
        if (string.IsNullOrWhiteSpace(code)) return result;

        var boundFieldNames = (await db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.FieldLookupType == lookupType && f.FieldName != null)
                .Select(f => f.FieldName!).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (boundFieldNames.Count > 0)
        {
            var rules = await db.FlagRules.AsNoTracking()
                .Where(r => r.ActiveFlag && r.StaticValue != null && r.LeftFieldCode != null)
                .Select(r => new { r.FlagId, r.LeftFieldCode, r.Operator, r.StaticValue }).ToListAsync(ct);

            foreach (var r in rules.Where(r => boundFieldNames.Contains(r.LeftFieldCode!)))
                if (LookupUsage.Tokens(r.StaticValue).Any(t => string.Equals(t, code, StringComparison.OrdinalIgnoreCase)))
                    result.rules.Add(new LookupValueRuleUsageDto { flagId = r.FlagId, fieldCode = r.LeftFieldCode, op = r.Operator });

            result.ruleUsageCount = result.rules.Count;
        }

        // Best-effort transaction scan — the code stored in a UDF blob for a
        // field bound to this type. Substring match (see plan §4.6); tolerant of
        // false positives, biased to protect in-use values from deletion.
        var needle = "\"" + code + "\"";
        result.txnUsageCount = await db.TransactionDetails.AsNoTracking()
            .CountAsync(d => d.UdfData != null && EF.Functions.Like(d.UdfData, "%" + needle + "%"), ct);

        result.inUse = result.ruleUsageCount > 0 || result.txnUsageCount > 0;
        return result;
    }
}
