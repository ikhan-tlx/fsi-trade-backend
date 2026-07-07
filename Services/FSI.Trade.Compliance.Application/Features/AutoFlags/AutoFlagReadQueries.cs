using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.AutoFlags;

// ===================== List ============================================
/// <summary>GET /api/v1/AutoFlag — all auto flags (Flag_Type=Automated) as summaries.</summary>
public class ListAutoFlagsQuery : IRequest<List<AutoFlagSummaryDto>> { }

public class ListAutoFlagsQueryHandler : IRequestHandler<ListAutoFlagsQuery, List<AutoFlagSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAutoFlagsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<AutoFlagSummaryDto>> Handle(ListAutoFlagsQuery req, CancellationToken ct)
    {
        var autoTypeId = await AutoFlagLookup.AutoTypeIdAsync(_db, ct);
        var sev = await AutoFlagLookup.SeverityNamesAsync(_db, ct);

        var flags = await _db.FlagCatalogues.AsNoTracking()
            .Where(f => f.FlagTypeLkpId == autoTypeId && !f.IsDeleted)
            .OrderByDescending(f => f.LastUpdatedDate ?? f.CreatedDate)
            .ToListAsync(ct);

        var ids = flags.Select(f => f.FlagId).ToList();

        var scopes = await _db.FlagScopes.AsNoTracking()
            .Where(s => ids.Contains(s.FlagId) && s.ActiveFlag && s.ProductId != null)
            .Select(s => new { s.FlagId, ProductId = s.ProductId!.Value })
            .ToListAsync(ct);
        var prodNames = await _db.Products.AsNoTracking()
            .Where(p => scopes.Select(s => s.ProductId).Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.ProductName })
            .ToListAsync(ct);
        var nameById = prodNames.ToDictionary(p => p.ProductId, p => p.ProductName);
        var scopeByFlag = scopes.GroupBy(s => s.FlagId)
            .ToDictionary(g => g.Key,
                g => g.Select(x => nameById.TryGetValue(x.ProductId, out var n) && !string.IsNullOrWhiteSpace(n)
                                    ? n : $"Product {x.ProductId}").Distinct().ToList());

        var ruleCounts = (await _db.FlagRules.AsNoTracking()
                .Where(r => ids.Contains(r.FlagId))
                .Select(r => r.FlagId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return flags.Select(f => new AutoFlagSummaryDto
        {
            flagId    = f.FlagId,
            flagCode  = f.FlagCode,
            name      = f.FlagName,
            severity  = f.SeverityLkpId is int sid && sev.TryGetValue(sid, out var sn) ? sn : "",
            status    = f.Status ?? (f.ActiveFlag ? "Active" : "Draft"),
            products  = scopeByFlag.TryGetValue(f.FlagId, out var p) ? p : new List<string>(),
            ruleCount = ruleCounts.TryGetValue(f.FlagId, out var rc) ? rc : 0,
            updatedBy = f.LastUpdatedBy ?? f.CreatedBy,
            updatedAt = (f.LastUpdatedDate ?? f.CreatedDate).ToString("o"),
        }).ToList();
    }
}

// ===================== GetById =========================================
/// <summary>GET /api/v1/AutoFlag/{id} — detail with rules + scope.</summary>
public record GetAutoFlagByIdQuery(int FlagId) : IRequest<AutoFlagDetailDto?>;

public class GetAutoFlagByIdQueryHandler : IRequestHandler<GetAutoFlagByIdQuery, AutoFlagDetailDto?>
{
    private readonly IApplicationDbContext _db;
    public GetAutoFlagByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<AutoFlagDetailDto?> Handle(GetAutoFlagByIdQuery req, CancellationToken ct)
    {
        var f = await _db.FlagCatalogues.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FlagId == req.FlagId && !x.IsDeleted, ct);
        if (f is null) return null;

        var sev = await AutoFlagLookup.SeverityNamesAsync(_db, ct);
        // Full scope incl. sub-product (tab); + legacy product-id list for back-compat.
        var scopeRows = await _db.FlagScopes.AsNoTracking()
            .Where(s => s.FlagId == f.FlagId && s.ActiveFlag)
            .Select(s => new { s.ProductId, s.TabId })
            .ToListAsync(ct);
        var scopes = scopeRows
            .Select(s => new ScopeEntryDto { productId = s.ProductId, tabId = s.TabId })
            .ToList();
        var scopeIds = scopeRows.Where(s => s.ProductId != null)
            .Select(s => s.ProductId!.Value).Distinct().ToList();

        var ruleRows = await _db.FlagRules.AsNoTracking()
            .Where(r => r.FlagId == f.FlagId)
            .OrderBy(r => r.RuleGroup).ThenBy(r => r.EvaluationOrder)
            .ToListAsync(ct);
        var rules = ruleRows.Select(r => new AutoFlagRuleDto
        {
            ruleId            = r.RuleId,
            ruleGroup         = r.RuleGroup,
            evaluationOrder   = r.EvaluationOrder,
            leftSource        = r.LeftSource,
            leftFieldCode     = r.LeftFieldCode,
            @operator         = r.Operator,
            rightOperandType  = r.RightOperandType,
            rightSource       = r.RightSource,
            rightFieldCode    = r.RightFieldCode,
            staticValue       = r.StaticValue,
            referenceListCode = r.ReferenceListCode,
            aggregateSpec     = r.AggregateSpec,
            handlerKey        = r.HandlerKey,
            rightFactor       = r.RightFactor,
            rightFactorUnit   = r.RightFactorUnit,
            logicalOperator   = r.LogicalOperator,
        }).ToList();

        return new AutoFlagDetailDto
        {
            flagId          = f.FlagId,
            flagCode        = f.FlagCode,
            name            = f.FlagName,
            description     = f.FlagDescription,
            severity        = f.SeverityLkpId is int sid && sev.TryGetValue(sid, out var sn) ? sn : "",
            status          = f.Status ?? (f.ActiveFlag ? "Active" : "Draft"),
            products        = scopeIds.Select(i => i.ToString()).ToList(),
            scopeProductIds = scopeIds,
            scopes          = scopes,
            raisePerMatch   = f.RaisePerMatch,
            updatedAt       = (f.LastUpdatedDate ?? f.CreatedDate).ToString("o"),
            rules           = rules,
        };
    }
}

// ===================== Operands ========================================
/// <summary>GET /api/v1/AutoFlag/Operands — the field catalogue for the builder.</summary>
public record ListAutoFlagOperandsQuery(int? ProductId) : IRequest<List<OperandOptionDto>>;

public class ListAutoFlagOperandsQueryHandler : IRequestHandler<ListAutoFlagOperandsQuery, List<OperandOptionDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAutoFlagOperandsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<OperandOptionDto>> Handle(ListAutoFlagOperandsQuery req, CancellationToken ct)
    {
        // Field types live in the FIELD lookup group; TenantFieldSetup.Field_Type_Lkp holds
        // the lookup's Hidden_Value (int). Rules only make sense on selectable/numeric
        // fields, so the operand catalogue is limited to Dropdown / SearchableDropdown /
        // Amount / Number. "Header" rows are not operands — they define the category each
        // field falls under (the section heading shown in the wizard).
        var typeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in await _db.Lookups.AsNoTracking()
                     .Where(l => l.LookupType == "FIELD")
                     .Select(l => new { l.VisibleValue, l.HiddenValue }).ToListAsync(ct))
            if (l.VisibleValue != null && int.TryParse(l.HiddenValue, out var hv) && !typeByName.ContainsKey(l.VisibleValue))
                typeByName[l.VisibleValue] = hv;

        int? T(string n) => typeByName.TryGetValue(n, out var v) ? v : (int?)null;
        var dropdown = T("Dropdown"); var searchable = T("SearchableDropdown");
        var amount = T("Amount"); var number = T("Number"); var headerType = T("Header");
        var allowed = new HashSet<int>(new[] { dropdown, searchable, amount, number }.Where(x => x != null).Select(x => x!.Value));

        var rows = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.IsActive && f.ProductId != null && f.TabId != null
                        && (req.ProductId == null || f.ProductId == req.ProductId))
            .Select(f => new { f.TenantFieldSetupId, f.ParentTenantFieldSetupId, f.ProductId, f.TabId, f.FieldSequence, f.FieldTypeLkp, f.FieldName, f.FieldLabel, f.LocaleFieldLabel, f.FieldTableName, f.FieldLookupType })
            .ToListAsync(ct);

        // Multi-value detection: a field is multi-value when it is a COLUMN inside a
        // repeating grid/table — i.e. its parent is a grid root. Grid roots carry the
        // table/grid field types (Field_Type_Lkp 27/40, matching the dynamic-form
        // renderer). Computed from config so it stays correct as the form changes.
        var gridRootTypes = new HashSet<int> { 27, 40 };
        var gridRootIds = rows
            .Where(r => r.FieldTypeLkp != null && gridRootTypes.Contains(r.FieldTypeLkp.Value))
            .Select(r => r.TenantFieldSetupId)
            .ToHashSet();

        // Tab + product display names.
        var tabIds = rows.Select(f => f.TabId!.Value).Distinct().ToList();
        var tabById = (await _db.Tabs.AsNoTracking()
                .Where(t => tabIds.Contains(t.TabId))
                .Select(t => new { t.TabId, t.TabName }).ToListAsync(ct))
            .GroupBy(t => t.TabId).ToDictionary(g => g.Key, g => g.First().TabName);
        var prodIds = rows.Select(f => f.ProductId!.Value).Distinct().ToList();
        var prodById = (await _db.Products.AsNoTracking()
                .Where(p => prodIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName }).ToListAsync(ct))
            .GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.First().ProductName);

        // Walk each (product, tab) in field-sequence order, tracking the running Header so
        // each operand is filed under its category. One option per (product, tab, code).
        var ordered = rows
            .OrderBy(r => r.ProductId).ThenBy(r => r.TabId).ThenBy(r => r.FieldSequence ?? int.MaxValue)
            .ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<OperandOptionDto>();
        int? curProd = null, curTab = null; var curHeader = "General";
        foreach (var r in ordered)
        {
            if (r.ProductId != curProd || r.TabId != curTab) { curProd = r.ProductId; curTab = r.TabId; curHeader = "General"; }

            if (headerType != null && r.FieldTypeLkp == headerType)
            {
                var hl = IsHuman(r.LocaleFieldLabel) ? r.LocaleFieldLabel
                       : IsHuman(r.FieldLabel) ? r.FieldLabel
                       : (!string.IsNullOrWhiteSpace(r.FieldName) ? Humanize(r.FieldName!) : null);
                if (!string.IsNullOrWhiteSpace(hl)) curHeader = hl!;
                continue;
            }
            if (r.FieldTypeLkp == null || !allowed.Contains(r.FieldTypeLkp.Value)) continue;
            if (string.IsNullOrWhiteSpace(r.FieldName)) continue;
            if (!seen.Add($"{r.ProductId}|{r.TabId}|{r.FieldName}")) continue;

            var configured = IsHuman(r.LocaleFieldLabel) ? r.LocaleFieldLabel
                           : IsHuman(r.FieldLabel) ? r.FieldLabel : null;
            var table = r.FieldTableName ?? "";
            var dt = (r.FieldTypeLkp == number || r.FieldTypeLkp == amount) ? "number" : "code";
            var ftype = r.FieldTypeLkp == dropdown ? "Dropdown"
                      : r.FieldTypeLkp == searchable ? "SearchableDropdown"
                      : r.FieldTypeLkp == amount ? "Amount"
                      : r.FieldTypeLkp == number ? "Number" : null;
            // KYC vs Trade is a property of the owning *product*, not the storage table.
            // KYC amount fields (e.g. "Total Annual Expected ... Turnover", KycParam3/4)
            // persist in the generic TmxTransactionDetail table, so the old
            // FieldTableName.Contains("KYC") test misfiled every KYC numeric field as Trade
            // and they never appeared under the KYC tab (only the 13 fields that happened to
            // sit in a KYC-named table did). Classify by the KYC product; keep the table-name
            // test as a fallback so nothing previously classified KYC regresses.
            var pname = r.ProductId is int pid && prodById.TryGetValue(pid, out var pn) ? pn : null;
            var isKyc = (pname != null && pname.IndexOf("KYC", StringComparison.OrdinalIgnoreCase) >= 0)
                        || table.IndexOf("KYC", StringComparison.OrdinalIgnoreCase) >= 0;
            list.Add(new OperandOptionDto
            {
                fieldCode   = r.FieldName!,
                label       = configured ?? Humanize(r.FieldName!),
                source      = isKyc ? "KYC" : "Trade",
                dataType    = dt,
                fieldType   = ftype,
                // The value-lookup group backing a dropdown field (Field_Lookup_Type). Lets the
                // builder bind the right operand to this field's own lookup values instead of
                // free text. Null/empty for non-lookup fields (amount/number/free-text).
                fieldLookupType = r.FieldLookupType,
                productId   = r.ProductId,
                productName = pname,
                tabId       = r.TabId,
                tabName     = r.TabId is int tid && tabById.TryGetValue(tid, out var tn) && !string.IsNullOrWhiteSpace(tn)
                                ? tn : $"Tab {r.TabId}",
                section     = curHeader,
                isMultiValue = r.ParentTenantFieldSetupId is int pid2 && gridRootIds.Contains(pid2),
            });
        }
        // Preserve walk order (product → tab → sequence) so the picker's Tab → category
        // grouping mirrors the wizard layout.
        return list;
    }

    /// <summary>A configured label is trusted only if it reads as a phrase
    /// (has whitespace) — guards against the legacy "label = another code" data.</summary>
    private static bool IsHuman(string? s)
        => !string.IsNullOrWhiteSpace(s) && s!.Trim().Contains(' ');

    /// <summary>Readable label derived from the field code: strip table prefix,
    /// split underscores + camelCase / acronym boundaries.</summary>
    private static string Humanize(string code)
    {
        var s = code;
        var us = s.IndexOf('_');
        if (us > 0 && us <= 5) s = s[(us + 1)..];        // drop short table prefix e.g. "EDL_"
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '_') { sb.Append(' '); continue; }
            // boundary: lower→Upper (BeneCountry → Bene Country) or
            // Upper run → Upper+lower (HScode → HS code)
            if (i > 0 && char.IsUpper(c) &&
                (!char.IsUpper(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}

// ===================== Reference lists =================================
/// <summary>GET /api/v1/AutoFlag/ReferenceLists — built-in REPO lists + custom lists.</summary>
public class ListAutoFlagReferenceListsQuery : IRequest<List<ReferenceListOptionDto>> { }

public class ListAutoFlagReferenceListsQueryHandler : IRequestHandler<ListAutoFlagReferenceListsQuery, List<ReferenceListOptionDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAutoFlagReferenceListsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ReferenceListOptionDto>> Handle(ListAutoFlagReferenceListsQuery req, CancellationToken ct)
    {
        var built = new List<ReferenceListOptionDto>
        {
            new() { code = "SANCTIONED_COUNTRIES", name = "Sanctioned / High-risk countries (REPO)" },
            new() { code = "SCREENING_GOODS",      name = "High-risk / screening goods — HS (REPO)" },
        };
        var custom = await _db.ReferenceLists.AsNoTracking()
            .Where(l => l.ActiveFlag)
            .Select(l => new ReferenceListOptionDto { code = l.ListCode, name = l.ListName })
            .ToListAsync(ct);
        return built.Concat(custom).ToList();
    }
}

// ===================== Sub-products (scope tree) =======================
/// <summary>GET /api/v1/AutoFlag/SubProducts — every product with its sub-product tabs,
/// for the scope editor. Sourced from TmX_Entity_Tab_Product_Mapping + Tabs + Products.</summary>
public class ListAutoFlagSubProductsQuery : IRequest<List<SubProductGroupDto>> { }

public class ListAutoFlagSubProductsQueryHandler : IRequestHandler<ListAutoFlagSubProductsQuery, List<SubProductGroupDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAutoFlagSubProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SubProductGroupDto>> Handle(ListAutoFlagSubProductsQuery req, CancellationToken ct)
    {
        var prods = (await _db.Products.AsNoTracking()
                .Select(p => new { p.ProductId, p.ProductName }).ToListAsync(ct))
            .GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.First().ProductName);

        var tabNames = (await _db.Tabs.AsNoTracking()
                .Select(t => new { t.TabId, t.TabName }).ToListAsync(ct))
            .GroupBy(t => t.TabId).ToDictionary(g => g.Key, g => g.First().TabName);

        var maps = await _db.EntityTabProductMappings.AsNoTracking()
            .Select(m => new { m.ProductId, m.TabId })
            .Distinct().ToListAsync(ct);

        return maps
            .GroupBy(m => m.ProductId)
            .Where(g => prods.ContainsKey(g.Key))
            .Select(g => new SubProductGroupDto
            {
                productId   = g.Key,
                productName = prods.TryGetValue(g.Key, out var pn) && !string.IsNullOrWhiteSpace(pn) ? pn : $"Product {g.Key}",
                tabs = g.Select(m => m.TabId).Distinct()
                        .Select(tid => new SubProductTabDto
                        {
                            tabId = tid,
                            tabName = tabNames.TryGetValue(tid, out var tn) && !string.IsNullOrWhiteSpace(tn) ? tn : $"Tab {tid}",
                        })
                        .OrderBy(t => t.tabName).ToList(),
            })
            .OrderBy(x => x.productName)
            .ToList();
    }
}

// ===================== shared lookup helpers ===========================
internal static class AutoFlagLookup
{
    public static async Task<int> AutoTypeIdAsync(IApplicationDbContext db, CancellationToken ct)
        => await db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "FLAG_TYPE" && l.HiddenValue == "AUTOMATED")
            .Select(l => l.Id).FirstOrDefaultAsync(ct);

    public static async Task<Dictionary<int, string>> SeverityNamesAsync(IApplicationDbContext db, CancellationToken ct)
    {
        // Dedupe-safe: TmX_Lookup can carry locale/tenant duplicates that would
        // make a naive ToDictionary throw on a repeated key.
        var rows = await db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "FLAG_SEVERITY")
            .Select(l => new { l.Id, l.VisibleValue })
            .ToListAsync(ct);
        var map = new Dictionary<int, string>();
        foreach (var r in rows) map[r.Id] = r.VisibleValue ?? "";
        return map;
    }

    /// <summary>Resolve a severity display/hidden string to its FLAG_SEVERITY lookup id.</summary>
    public static async Task<int?> SeverityIdAsync(IApplicationDbContext db, string? severity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(severity)) return null;
        var hidden = severity.Trim().ToUpperInvariant();
        return await db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "FLAG_SEVERITY"
                        && (l.HiddenValue == hidden || l.VisibleValue == severity))
            .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
    }
}
