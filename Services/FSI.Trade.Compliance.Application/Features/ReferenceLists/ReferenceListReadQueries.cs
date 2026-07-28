using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.LookupAdmin;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.ReferenceLists;

internal static class RefJson
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static List<UdfFieldDto> ParseSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<UdfFieldDto>>(json!, Opts) ?? new(); }
        catch { return new(); }
    }

    public static Dictionary<string, object?> ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json!);
            var d = new Dictionary<string, object?>();
            foreach (var p in doc.RootElement.EnumerateObject())
                d[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l : p.Value.GetDouble(),
                    JsonValueKind.True   => true,
                    JsonValueKind.False  => false,
                    JsonValueKind.Null   => null,
                    _                    => p.Value.ToString(),
                };
            return d;
        }
        catch { return new(); }
    }
}

// ═══════════════ Domains ═══════════════════════════════════════════════════
/// <summary>GET /api/v1/ReferenceList/Domains — distinct Lookup_Type groups in use.</summary>
public class ListReferenceDomainsQuery : IRequest<List<DomainDto>> { }

public class ListReferenceDomainsQueryHandler : IRequestHandler<ListReferenceDomainsQuery, List<DomainDto>>
{
    public Task<List<DomainDto>> Handle(ListReferenceDomainsQuery req, CancellationToken ct)
    {
        // Domains are populated from the SAME static allow-list that governs Lookup Management
        // (LookupAdminAllowList.Types) — the single compiled source of truth for which TmX_Lookup
        // groups are user-managed — rather than every distinct Lookup_Type in the catalogue. This
        // keeps the reference-list "Domain" dropdown aligned with the managed lookup set so an
        // author can only scope a reference type to a governed lookup group.
        var list = LookupAdminAllowList.Types
            .Select(l => new DomainDto { key = l, label = DomainLabels.For(l) })
            .GroupBy(d => d.key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(d => d.label).ToList();

        // Preserve the ungoverned "custom / none" option so free-text reference lists (LookupType
        // = null) remain creatable — it is not a TmX_Lookup group, so add it explicitly.
        list.Insert(0, new DomainDto { key = "", label = DomainLabels.For(null) });
        return Task.FromResult(list);
    }
}

// ═══════════════ Types list ═════════════════════════════════════════════════
/// <summary>GET /api/v1/ReferenceList/Types — paged/filtered/sorted category grid.</summary>
public class ListReferenceTypesQuery : PagedQuery, IRequest<PagedResult<ReferenceTypeSummaryDto>>
{
    public string? Domain { get; set; }   // Lookup_Type ("" / "custom" = literal); null = all
    public string? Status { get; set; }   // active | inactive | all
}

public class ListReferenceTypesQueryHandler : IRequestHandler<ListReferenceTypesQuery, PagedResult<ReferenceTypeSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    public ListReferenceTypesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<ReferenceTypeSummaryDto>> Handle(ListReferenceTypesQuery req, CancellationToken ct)
    {
        var types = await _db.ReferenceTypes.AsNoTracking().ToListAsync(ct);

        // counts
        var valueCounts = (await _db.ReferenceValueTypeMaps.AsNoTracking()
                .Where(m => m.ActiveFlag)
                .Select(m => m.ReferenceTypeId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var ruleRefs = await _db.FlagRules.AsNoTracking()
            .Where(r => r.ActiveFlag && r.ReferenceListCode != null)
            .Select(r => new { r.ReferenceListCode, r.FlagId }).ToListAsync(ct);
        var usedBy = ruleRefs.GroupBy(r => r.ReferenceListCode!.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Select(x => x.FlagId).Distinct().Count());

        var rows = types.Select(t => new ReferenceTypeSummaryDto
        {
            id = t.ReferenceTypeId, code = t.TypeCode, name = t.TypeName, description = t.Description,
            lookupType = t.LookupType, domainLabel = DomainLabels.For(t.LookupType),
            valueCount = valueCounts.TryGetValue(t.ReferenceTypeId, out var vc) ? vc : 0,
            usedByCount = usedBy.TryGetValue(t.TypeCode.ToUpperInvariant(), out var ub) ? ub : 0,
            active = t.ActiveFlag, source = t.Source,
        }).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim().ToLowerInvariant();
            rows = rows.Where(r => (r.name + " " + r.code + " " + (r.description ?? "")).ToLowerInvariant().Contains(f));
        }
        if (!string.IsNullOrWhiteSpace(req.Domain) && !string.Equals(req.Domain, "all", StringComparison.OrdinalIgnoreCase))
        {
            var dom = req.Domain is "custom" or "literal" ? "" : req.Domain;
            rows = rows.Where(r => (r.lookupType ?? "") == dom);
        }
        if (!string.IsNullOrWhiteSpace(req.Status) && !string.Equals(req.Status, "all", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r => r.active == string.Equals(req.Status, "active", StringComparison.OrdinalIgnoreCase));

        rows = SortTypes(rows, req.Sort);
        var all = rows.ToList();
        var total = all.Count;
        var page = all.Skip(req.Skip).Take(req.Take).ToList();
        return new PagedResult<ReferenceTypeSummaryDto> { items = page, total = total, page = req.Page, pageSize = req.PageSize };
    }

    private static IEnumerable<ReferenceTypeSummaryDto> SortTypes(IEnumerable<ReferenceTypeSummaryDto> q, string? sort)
    {
        var (field, desc) = ParseSort(sort, "name");
        Func<ReferenceTypeSummaryDto, object> key = field switch
        {
            "code"   => r => r.code,
            "values" => r => r.valueCount,
            "used"   => r => r.usedByCount,
            "domain" => r => r.domainLabel,
            "status" => r => r.active,
            _        => r => r.name,
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

// ═══════════════ Type detail ════════════════════════════════════════════════
public record GetReferenceTypeByIdQuery(long Id) : IRequest<ReferenceTypeDetailDto?>;

public class GetReferenceTypeByIdQueryHandler : IRequestHandler<GetReferenceTypeByIdQuery, ReferenceTypeDetailDto?>
{
    private readonly IApplicationDbContext _db;
    public GetReferenceTypeByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ReferenceTypeDetailDto?> Handle(GetReferenceTypeByIdQuery req, CancellationToken ct)
    {
        var t = await _db.ReferenceTypes.AsNoTracking().FirstOrDefaultAsync(x => x.ReferenceTypeId == req.Id, ct);
        if (t is null) return null;

        var members = await (from m in _db.ReferenceValueTypeMaps.AsNoTracking()
                             join v in _db.ReferenceValues.AsNoTracking() on m.ReferenceValueId equals v.ReferenceValueId
                             where m.ReferenceTypeId == t.ReferenceTypeId
                             orderby v.Name
                             select new TypeMemberDto { valueId = v.ReferenceValueId, code = v.Code, name = v.Name ?? v.Code, active = m.ActiveFlag })
                            .ToListAsync(ct);

        var used = await _db.FlagRules.AsNoTracking()
            .Where(r => r.ActiveFlag && r.ReferenceListCode == t.TypeCode)
            .Select(r => r.FlagId).Distinct().CountAsync(ct);

        return new ReferenceTypeDetailDto
        {
            id = t.ReferenceTypeId, code = t.TypeCode, name = t.TypeName, description = t.Description,
            lookupType = t.LookupType, domainLabel = DomainLabels.For(t.LookupType),
            active = t.ActiveFlag, source = t.Source, usedByCount = used,
            schema = RefJson.ParseSchema(t.UdfSchema), members = members,
        };
    }
}

// ═══════════════ Values list ════════════════════════════════════════════════
public class ListReferenceValuesQuery : PagedQuery, IRequest<PagedResult<ReferenceValueSummaryDto>>
{
    public string? Domain   { get; set; }   // Lookup_Type
    public long?   Category { get; set; }   // Reference_Type_Id
}

public class ListReferenceValuesQueryHandler : IRequestHandler<ListReferenceValuesQuery, PagedResult<ReferenceValueSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    public ListReferenceValuesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<ReferenceValueSummaryDto>> Handle(ListReferenceValuesQuery req, CancellationToken ct)
    {
        var typeById = await _db.ReferenceTypes.AsNoTracking()
            .Select(t => new { t.ReferenceTypeId, t.TypeCode, t.TypeName }).ToDictionaryAsync(t => t.ReferenceTypeId, ct);
        var maps = await _db.ReferenceValueTypeMaps.AsNoTracking()
            .Select(m => new { m.ReferenceValueId, m.ReferenceTypeId }).ToListAsync(ct);
        var catsByValue = maps.GroupBy(m => m.ReferenceValueId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ReferenceTypeId).ToList());

        var values = await _db.ReferenceValues.AsNoTracking().ToListAsync(ct);

        var rows = values.Select(v =>
        {
            var chips = (catsByValue.TryGetValue(v.ReferenceValueId, out var tids) ? tids : new())
                .Where(typeById.ContainsKey)
                .Select(tid => new TypeChipDto { id = tid, code = typeById[tid].TypeCode, name = typeById[tid].TypeName })
                .OrderBy(c => c.name).ToList();
            return new ReferenceValueSummaryDto
            {
                id = v.ReferenceValueId, code = v.Code, name = v.Name,
                lookupType = v.LookupType, domainLabel = DomainLabels.For(v.LookupType),
                active = v.ActiveFlag, categories = chips,
            };
        }).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim().ToLowerInvariant();
            rows = rows.Where(r => (r.code + " " + (r.name ?? "")).ToLowerInvariant().Contains(f));
        }
        if (!string.IsNullOrWhiteSpace(req.Domain) && !string.Equals(req.Domain, "all", StringComparison.OrdinalIgnoreCase))
        {
            var dom = req.Domain is "custom" or "literal" ? "" : req.Domain;
            rows = rows.Where(r => (r.lookupType ?? "") == dom);
        }
        if (req.Category is { } catId)
            rows = rows.Where(r => r.categories.Any(c => c.id == catId));

        var (field, desc) = ListReferenceTypesQueryHandler.ParseSort(req.Sort, "name");
        Func<ReferenceValueSummaryDto, object> key = field switch
        {
            "code" => r => r.code,
            "cats" => r => r.categories.Count,
            "domain" => r => r.domainLabel,
            "status" => r => r.active,
            _ => r => r.name ?? r.code,
        };
        rows = desc ? rows.OrderByDescending(key) : rows.OrderBy(key);

        var all = rows.ToList();
        return new PagedResult<ReferenceValueSummaryDto>
        {
            items = all.Skip(req.Skip).Take(req.Take).ToList(), total = all.Count, page = req.Page, pageSize = req.PageSize,
        };
    }
}

// ═══════════════ Value detail ═══════════════════════════════════════════════
public record GetReferenceValueByIdQuery(long Id) : IRequest<ReferenceValueDetailDto?>;

public class GetReferenceValueByIdQueryHandler : IRequestHandler<GetReferenceValueByIdQuery, ReferenceValueDetailDto?>
{
    private readonly IApplicationDbContext _db;
    public GetReferenceValueByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ReferenceValueDetailDto?> Handle(GetReferenceValueByIdQuery req, CancellationToken ct)
    {
        var v = await _db.ReferenceValues.AsNoTracking().FirstOrDefaultAsync(x => x.ReferenceValueId == req.Id, ct);
        if (v is null) return null;

        var typeIds = await _db.ReferenceValueTypeMaps.AsNoTracking()
            .Where(m => m.ReferenceValueId == v.ReferenceValueId).Select(m => m.ReferenceTypeId).ToListAsync(ct);
        var types = await _db.ReferenceTypes.AsNoTracking().Where(t => typeIds.Contains(t.ReferenceTypeId)).ToListAsync(ct);

        var chips = types.Select(t => new TypeChipDto { id = t.ReferenceTypeId, code = t.TypeCode, name = t.TypeName })
            .OrderBy(c => c.name).ToList();

        // union schema across selected types (first field per key wins)
        var union = new List<UdfFieldDto>(); var seen = new HashSet<string>();
        foreach (var t in types)
            foreach (var f in RefJson.ParseSchema(t.UdfSchema))
                if (f.key is { Length: > 0 } && seen.Add(f.key)) union.Add(f);

        return new ReferenceValueDetailDto
        {
            id = v.ReferenceValueId, code = v.Code, name = v.Name, description = v.Description,
            lookupType = v.LookupType, domainLabel = DomainLabels.For(v.LookupType), active = v.ActiveFlag,
            udf = RefJson.ParseData(v.UdfData), typeIds = typeIds, categories = chips, schema = union,
        };
    }
}

// ═══════════════ Lookup picker (Code = Hidden_Value) ════════════════════════
public record ListLookupPicksQuery(string LookupType, string? Q) : IRequest<List<LookupPickDto>>;

public class ListLookupPicksQueryHandler : IRequestHandler<ListLookupPicksQuery, List<LookupPickDto>>
{
    private readonly IApplicationDbContext _db;
    public ListLookupPicksQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<LookupPickDto>> Handle(ListLookupPicksQuery req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.LookupType)) return new();
        var rows = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == req.LookupType && l.HiddenValue != null)
            .Select(l => new { l.HiddenValue, l.VisibleValue }).ToListAsync(ct);
        var picks = rows
            .GroupBy(r => r.HiddenValue!)
            .Select(g => new LookupPickDto { code = g.Key, name = g.First().VisibleValue ?? g.Key });
        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var q = req.Q.Trim().ToLowerInvariant();
            picks = picks.Where(p => (p.code + " " + p.name).ToLowerInvariant().Contains(q));
        }
        return picks.OrderBy(p => p.name).Take(50).ToList();
    }
}

// ═══════════════ Used-by rules ══════════════════════════════════════════════
public record GetReferenceTypeUsageQuery(string Code) : IRequest<List<ReferenceRuleUsageDto>>;

public class GetReferenceTypeUsageQueryHandler : IRequestHandler<GetReferenceTypeUsageQuery, List<ReferenceRuleUsageDto>>
{
    private readonly IApplicationDbContext _db;
    public GetReferenceTypeUsageQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ReferenceRuleUsageDto>> Handle(GetReferenceTypeUsageQuery req, CancellationToken ct)
    {
        var rules = await _db.FlagRules.AsNoTracking()
            .Where(r => r.ActiveFlag && r.ReferenceListCode == req.Code)
            .Select(r => new { r.FlagId, r.Operator }).ToListAsync(ct);
        if (rules.Count == 0) return new();

        var flagIds = rules.Select(r => r.FlagId).Distinct().ToList();
        var flags = await _db.FlagCatalogues.AsNoTracking()
            .Where(f => flagIds.Contains(f.FlagId))
            .Select(f => new { f.FlagId, f.FlagName, f.Status, f.ActiveFlag }).ToListAsync(ct);
        var flagById = flags.ToDictionary(f => f.FlagId);

        return rules.GroupBy(r => r.FlagId).Select(g =>
        {
            flagById.TryGetValue(g.Key, out var f);
            return new ReferenceRuleUsageDto
            {
                flagId = g.Key,
                name = f?.FlagName ?? $"Flag #{g.Key}",
                op = g.Select(x => x.Operator).FirstOrDefault() ?? "In",
                conditions = g.Count(),
                status = f?.Status ?? (f?.ActiveFlag == true ? "Active" : "Draft"),
            };
        }).OrderBy(x => x.name).ToList();
    }
}

// ═══════════════ Field domains (rule-builder preview) ═══════════════════════
public class ListFieldDomainsQuery : IRequest<List<FieldDomainDto>> { }

public class ListFieldDomainsQueryHandler : IRequestHandler<ListFieldDomainsQuery, List<FieldDomainDto>>
{
    private readonly IApplicationDbContext _db;
    public ListFieldDomainsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<FieldDomainDto>> Handle(ListFieldDomainsQuery req, CancellationToken ct)
    {
        // One representative lookup-backed field per distinct domain, plus a couple
        // of non-lookup (literal/numeric) fields so the preview shows the "operator
        // not offered" case. Enough to demonstrate domain-filtered RHS.
        var lookupFields = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.FieldLookupType != null && f.FieldName != null)
            .Select(f => new { f.FieldName, f.FieldLookupType }).ToListAsync(ct);

        var perDomain = lookupFields
            .GroupBy(f => f.FieldLookupType!)
            .Select(g => g.OrderBy(x => x.FieldName).First())
            .Select(f => new FieldDomainDto
            {
                fieldCode = f.FieldName!, label = Humanize(f.FieldName!),
                lookupType = f.FieldLookupType, domainLabel = DomainLabels.For(f.FieldLookupType),
            })
            .OrderBy(f => f.domainLabel).Take(8).ToList();

        var literal = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.FieldLookupType == null && f.FieldName != null)
            .Select(f => f.FieldName).FirstOrDefaultAsync(ct);
        if (literal != null)
            perDomain.Add(new FieldDomainDto { fieldCode = literal, label = Humanize(literal), lookupType = null, domainLabel = "—" });

        return perDomain;
    }

    private static string Humanize(string code)
    {
        var s = code.Replace('_', ' ').Trim();
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
}
