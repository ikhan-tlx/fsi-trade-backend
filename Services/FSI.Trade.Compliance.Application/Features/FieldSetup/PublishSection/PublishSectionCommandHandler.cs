using FSI.Trade.Compliance.Application.Common.Caching;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.FieldSetup.Common;
using FSI.Trade.Compliance.Domain.Entities.Forms;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.FieldSetup.PublishSection;

/// <summary>
/// Applies a Field Configuration "Review &amp; publish" batch in one
/// transaction. See <see cref="PublishSectionCommand"/> for the contract +
/// guardrail rules. Sequence is re-emitted with gaps of <see cref="SeqGap"/>.
/// </summary>
public class PublishSectionCommandHandler
    : IRequestHandler<PublishSectionCommand, PublishSectionResult>
{
    /// <summary>Gap between consecutive Field_Sequence values, so a later single
    /// insert doesn't force a full renumber.</summary>
    private const int SeqGap = 10;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public PublishSectionCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<PublishSectionResult> Handle(PublishSectionCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException(
                "unauthenticated",
                "Field configuration edits require an authenticated caller.");

        var now = DateTime.UtcNow;

        // Split the batch: edits of existing rows vs brand-new INSERTS.
        var existingItems = req.items.Where(i => !i.isNew && i.tenantFieldSetupId > 0).ToList();
        var newItems      = req.items.Where(i => i.isNew).ToList();

        var ids = existingItems.Select(i => i.tenantFieldSetupId).ToList();

        // Tracked load of every EXISTING row in the batch (single round-trip).
        var entities = await _db.TenantFieldSetups
            .Where(f => ids.Contains(f.TenantFieldSetupId))
            .ToListAsync(ct);

        var byId = entities.ToDictionary(e => e.TenantFieldSetupId);

        // Every referenced existing row must exist AND belong to the stated product —
        // stops a caller editing fields across products by guessing ids.
        foreach (var item in existingItems)
        {
            if (!byId.TryGetValue(item.tenantFieldSetupId, out var e))
                throw new NotFoundException(
                    "field_setup_not_found",
                    $"TenantFieldSetup {item.tenantFieldSetupId} does not exist.");

            if (e.ProductId != req.productId)
                throw new ValidationException(
                    "field_product_mismatch",
                    $"Field {item.tenantFieldSetupId} does not belong to product {req.productId}.");
        }

        // Validate any requested parent moves point at a row in the same product.
        foreach (var item in existingItems.Where(i => i.moveParent && i.parentTenantFieldSetupId.HasValue))
        {
            var parentId = item.parentTenantFieldSetupId!.Value;
            var parent = byId.TryGetValue(parentId, out var p)
                ? p
                : await _db.TenantFieldSetups
                    .FirstOrDefaultAsync(f => f.TenantFieldSetupId == parentId, ct);
            if (parent is null || parent.ProductId != req.productId)
                throw new ValidationException(
                    "invalid_parent",
                    $"Parent {parentId} is not a valid field in product {req.productId}.");
        }

        int updated = 0, reordered = 0, retyped = 0, moved = 0, inserted = 0;
        // (product, tab, chosenFieldName) that were designated the amount field this batch —
        // used to enforce one-amount-field-per-sub-product across locale rows / prior state.
        var amountGroups = new List<(int? pid, int? tab, string? field)>();

        // ---- per-item edits + guardrails (existing rows only) -------------
        foreach (var item in existingItems)
        {
            var e = byId[item.tenantFieldSetupId];
            var touched = false;

            if (item.fieldLabel is not null && e.FieldLabel != item.fieldLabel.Trim())
            {
                e.FieldLabel = item.fieldLabel.Trim();
                touched = true;
            }

            if (item.placeholder is not null)
            {
                var ph = item.placeholder.Length == 0 ? null : item.placeholder.Trim();
                if (e.Placeholder != ph) { e.Placeholder = ph; touched = true; }
            }

            if (item.isActive.HasValue && e.IsActive != item.isActive.Value)
            {
                e.IsActive = item.isActive.Value;
                touched = true;
            }

            // Required — only over a literal/empty rule; a formula is platform-managed.
            if (item.isMandatory.HasValue)
            {
                if (FieldTypeSafeMatrix.IsFormula(e.IsMandatory))
                    throw new ConflictException(
                        "field_has_conditional_rule",
                        $"Field '{e.FieldName}' has a conditional Is_Mandatory rule and cannot be " +
                        "toggled here. Contact the platform team.");
                var next = item.isMandatory.Value ? "true" : "false";
                if (!string.Equals(e.IsMandatory, next, StringComparison.OrdinalIgnoreCase))
                {
                    e.IsMandatory = next;
                    touched = true;
                }
            }

            // Type — only within the safe matrix, never for lookup-bound or
            // formula-managed fields.
            if (item.fieldTypeLkp.HasValue && item.fieldTypeLkp.Value != (e.FieldTypeLkp ?? 0))
            {
                var target = item.fieldTypeLkp.Value;
                if (!FieldTypeSafeMatrix.IsSafeConversion(e.FieldTypeLkp, target))
                    throw new ConflictException(
                        "unsafe_type_change",
                        $"Field '{e.FieldName}' cannot be converted to type {target} — only " +
                        "platform-safe conversions (e.g. Text ↔ Long text) are allowed here.");
                if (!string.IsNullOrWhiteSpace(e.FieldLookupType))
                    throw new ConflictException(
                        "unsafe_type_change",
                        $"Field '{e.FieldName}' is bound to the '{e.FieldLookupType}' lookup; " +
                        "changing its type would drop the binding.");
                if (FieldTypeSafeMatrix.IsPlatformManaged(e.IsMandatory, e.Visibility, e.IsDisabled))
                    throw new ConflictException(
                        "unsafe_type_change",
                        $"Field '{e.FieldName}' is formula-managed; its type is controlled by the platform.");
                e.FieldTypeLkp = target;
                retyped++;
                touched = true;
            }

            // Auto-resolved amount field (per-sub-product radio). Numeric-only.
            if (item.isAmountField.HasValue && e.IsAmountField != item.isAmountField.Value)
            {
                if (item.isAmountField.Value && !await IsAmountEligibleAsync(e.FieldTypeLkp, ct))
                    throw new ConflictException(
                        "field_not_amount_type",
                        $"Field '{e.FieldName}' is not a numeric/amount field, so it can't be the " +
                        "auto-resolved amount field.");
                e.IsAmountField = item.isAmountField.Value;
                touched = true;
                if (item.isAmountField.Value)
                    amountGroups.Add((e.ProductId, e.TabId, e.FieldName));
            }

            // Parent / section move (explicit opt-in via moveParent).
            if (item.moveParent && e.ParentTenantFieldSetupId != item.parentTenantFieldSetupId)
            {
                e.ParentTenantFieldSetupId = item.parentTenantFieldSetupId;
                moved++;
                touched = true;
            }

            if (touched)
            {
                e.LastUpdatedBy   = userId;
                e.LastUpdatedDate = now;
                updated++;
            }
        }

        // ---- INSERTS (Add field / Add section) ----------------------------
        // New rows are keyed within the batch by tempId so children can point at a
        // new parent (topological: parents get real ids on the first SaveChanges,
        // then deferred child→parent links are resolved on a second save).
        var byTempId        = new Dictionary<int, TenantFieldSetup>();
        var deferredParents = new List<(int childTemp, int parentTemp)>();

        if (newItems.Count > 0)
        {
            if (req.tabId is null)
                throw new ValidationException("tab_required",
                    "A tab (sub-type) is required to add new fields.");
            var tabId = req.tabId;

            // Template row → Tenant_Id / Locale_Id / the section's flat Field_Table_Name.
            var template = await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.ProductId == req.productId && f.TabId == tabId)
                .OrderBy(f => f.FieldSequence)
                .FirstOrDefaultAsync(ct);
            template ??= await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.ProductId == req.productId)
                .FirstOrDefaultAsync(ct);

            var tenantId = template?.TenantId ?? 1;
            var localeId = template?.LocaleId  ?? 1;

            // Field_Name UNIQUENESS is enforced across the WHOLE product, while the
            // generated prefix still follows the (Product, Tab) sub-product convention.
            //
            // Bug this guards against: two field sections added separately under different
            // sub-products of the same product were each name-checked ONLY within their own
            // tab, so both could be minted with the same auto-generated Field_Name (e.g.
            // "ELCAF_Quantity" under both Export LC Amendment and Export Collections — the
            // tab's most-common prefix, not the tab's own code). The auto-flag operand
            // catalogue and rule evaluation identify a field by Field_Name across the
            // product, so the duplicate surfaced as ONE field appearing under two
            // sub-products in the "Choose a field" picker. Deduping the generated/explicit
            // name against every Field_Name in the product (not just this tab) makes a fresh
            // field product-unique (a collision gets a numeric suffix), while `prefix` stays
            // tab-derived so the sub-product naming convention is preserved.
            var tabNames = await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.ProductId == req.productId && f.TabId == tabId && f.FieldName != null)
                .Select(f => f.FieldName!)
                .ToListAsync(ct);
            var productNames = await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.ProductId == req.productId && f.FieldName != null)
                .Select(f => f.FieldName!)
                .ToListAsync(ct);
            var taken  = new HashSet<string>(productNames, StringComparer.OrdinalIgnoreCase);
            var prefix = DerivePrefix(tabNames);

            // The section's FLAT value-table (e.g. "TmxTransactionDetail[]") = the most common
            // non-null Field_Table_Name among existing DATA fields in this (Product, Tab). We must
            // NOT inherit from the first-by-sequence row (usually a header → null table), or a new
            // flat field's value would not be routed to the value store on transaction save.
            var flatTableName = (await _db.TenantFieldSetups.AsNoTracking()
                    .Where(f => f.ProductId == req.productId && f.TabId == tabId && f.FieldTableName != null
                                && f.FieldTypeLkp != 30 && f.FieldTypeLkp != 27 && f.FieldTypeLkp != 4)
                    .Select(f => f.FieldTableName!)
                    .ToListAsync(ct))
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                ?? template?.FieldTableName;

            // Existing grid keys in this (Product, Tab) — so a new table gets a unique one.
            var existingTables = new HashSet<string>(
                await _db.TenantFieldSetups.AsNoTracking()
                    .Where(f => f.ProductId == req.productId && f.TabId == tabId && f.FieldTableName != null)
                    .Select(f => f.FieldTableName!).Distinct().ToListAsync(ct),
                StringComparer.OrdinalIgnoreCase);
            // tempId of a NEW table root → its generated grid key, so its NEW columns inherit it.
            var tempTableName = new Dictionary<int, string>();

            foreach (var item in newItems)
            {
                var lkp = item.fieldTypeLkp
                    ?? throw new ValidationException("field_type_required",
                        "A field type is required to add a new field.");
                if (!IsAllowedNewType(lkp))
                    throw new ValidationException("field_type_unsupported",
                        $"Field type {lkp} cannot be added here.");

                // Parent: an existing real row, or a new parent by tempId (deferred).
                int? parentId = null;
                if (item.parentTempId.HasValue)
                {
                    if (!newItems.Any(n => n.tempId == item.parentTempId))
                        throw new ValidationException("invalid_parent",
                            $"parentTempId {item.parentTempId} is not part of this batch.");
                }
                else if (item.parentTenantFieldSetupId.HasValue)
                {
                    var pid = item.parentTenantFieldSetupId.Value;
                    var parent = byId.TryGetValue(pid, out var pp)
                        ? pp
                        : await _db.TenantFieldSetups.FirstOrDefaultAsync(f => f.TenantFieldSetupId == pid, ct);
                    if (parent is null || parent.ProductId != req.productId)
                        throw new ValidationException("invalid_parent",
                            $"Parent {pid} is not a valid field in product {req.productId}.");
                    parentId = pid;
                }

                // Dropdown must bind to an existing lookup type.
                string? lookupType = null;
                if (lkp == 25)
                {
                    lookupType = item.fieldLookupType?.Trim();
                    if (string.IsNullOrWhiteSpace(lookupType))
                        throw new ValidationException("lookup_required",
                            "A dropdown field must bind to a lookup type.");
                    var lkExists = await _db.Lookups.AsNoTracking()
                        .AnyAsync(l => l.LookupType == lookupType, ct);
                    if (!lkExists)
                        throw new ValidationException("lookup_not_found",
                            $"Lookup type '{lookupType}' does not exist.");
                }

                // Field_Name: explicit (validated) or generated (prefixed, unique).
                var name = BuildFieldName(item.fieldName, item.fieldLabel, lkp, prefix, taken);
                taken.Add(name);

                // Field_Table_Name: inherit from the parent grid (column) or the section (flat).
                var tableName = string.IsNullOrWhiteSpace(item.fieldTableName) ? null : item.fieldTableName!.Trim();
                if (tableName is null)
                {
                    if (lkp == 27)
                    {
                        // New table root → its own unique grid value-array key.
                        tableName = GenerateGridKey(item.fieldLabel, existingTables);
                        if (item.tempId.HasValue) tempTableName[item.tempId.Value] = tableName;
                    }
                    else if (item.parentTempId.HasValue && tempTableName.TryGetValue(item.parentTempId.Value, out var newGrid))
                        tableName = newGrid;                      // column of a NEW table → its grid key
                    else if (parentId.HasValue && byId.TryGetValue(parentId.Value, out var pe) && pe.FieldTypeLkp == 27)
                        tableName = pe.FieldTableName;            // column of an EXISTING table
                    else
                        tableName = flatTableName;                // flat field → section value-table
                }

                var label        = (item.fieldLabel ?? "").Trim();
                var isStructural = lkp is 30 or 4 or 27;          // header / subtext / table root

                var e = new TenantFieldSetup
                {
                    TenantId                 = tenantId,
                    ProductId                = req.productId,
                    TabId                    = tabId,
                    ParentTenantFieldSetupId = parentId,
                    FieldName                = name,
                    FieldLabel               = label,
                    FieldTypeLkp             = lkp,
                    FieldTableName           = tableName,
                    FieldLookupType          = lookupType,
                    Placeholder              = string.IsNullOrWhiteSpace(item.placeholder) ? null : item.placeholder!.Trim(),
                    IsActive                 = item.isActive ?? true,
                    IsMandatory              = isStructural ? null : ((item.isMandatory ?? false) ? "true" : "false"),
                    LocaleId                 = localeId,
                    LocaleLabel              = label,
                    LocaleFieldLabel         = label,
                    CreatedBy                = userId,
                    CreatedDate              = now,
                    LastUpdatedBy            = userId,
                    LastUpdatedDate          = now,
                };

                _db.TenantFieldSetups.Add(e);
                inserted++;

                if (item.tempId.HasValue)
                {
                    byTempId[item.tempId.Value] = e;
                    if (item.parentTempId.HasValue)
                        deferredParents.Add((item.tempId.Value, item.parentTempId.Value));
                }
            }
        }

        // ---- re-emit Field_Sequence from display order (gaps of 10) --------
        // Covers existing + new rows (new resolved by tempId).
        var ordered = req.items.OrderBy(i => i.order).ToList();
        for (var rank = 0; rank < ordered.Count; rank++)
        {
            var it = ordered[rank];
            TenantFieldSetup? e =
                (!it.isNew && byId.TryGetValue(it.tenantFieldSetupId, out var ex)) ? ex
                : (it.tempId.HasValue && byTempId.TryGetValue(it.tempId.Value, out var nw)) ? nw
                : null;
            if (e is null) continue;

            var nextSeq = (rank + 1) * SeqGap;
            if ((e.FieldSequence ?? -1) != nextSeq)
            {
                e.FieldSequence = nextSeq;
                if (e.LastUpdatedDate != now) { e.LastUpdatedBy = userId; e.LastUpdatedDate = now; if (!it.isNew) updated++; }
                if (!it.isNew) reordered++;
            }
        }

        // Enforce one amount field per sub-product: clear the flag on every OTHER field
        // (incl. its locale rows / prior designations) in each (Product, Tab) just set.
        foreach (var g in amountGroups.Distinct())
        {
            var others = await _db.TenantFieldSetups
                .Where(f => f.ProductId == g.pid
                            && (g.tab == null ? f.TabId == null : f.TabId == g.tab)
                            && f.FieldName != g.field
                            && f.IsAmountField)
                .ToListAsync(ct);
            foreach (var o in others) { o.IsAmountField = false; o.LastUpdatedBy = userId; o.LastUpdatedDate = now; }
        }

        await _db.SaveChangesAsync(ct);

        // Resolve new-parent (temp id) links now that inserts have real ids, then re-save.
        if (deferredParents.Count > 0)
        {
            foreach (var (childTemp, parentTemp) in deferredParents)
                if (byTempId.TryGetValue(childTemp, out var child) &&
                    byTempId.TryGetValue(parentTemp, out var parent))
                    child.ParentTenantFieldSetupId = parent.TenantFieldSetupId;
            await _db.SaveChangesAsync(ct);
        }

        // Bust read-side form-schema caches for this product (all cultures).
        FormSchemaCacheVersion.Bump(req.productId);

        return new PublishSectionResult
        {
            productId = req.productId,
            updated   = updated,
            reordered = reordered,
            retyped   = retyped,
            moved     = moved,
            inserted  = inserted,
        };
    }

    private static readonly System.Text.RegularExpressions.Regex NameRx =
        new("^[A-Za-z][A-Za-z0-9_]{0,49}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Types an admin may ADD here: Header 30, SubText 4, Text 3, Long text 24,
    /// Amount/number 64, Date 26, Dropdown 25, and a new Table root 27 (with its columns).</summary>
    private static bool IsAllowedNewType(int lkp) => lkp is 30 or 4 or 3 or 24 or 64 or 26 or 25 or 27;

    /// <summary>Generate a unique grid value-array key for a NEW table (convention
    /// <c>Tmx&lt;Label&gt;[]</c>, matching existing grids like <c>TmxBeneficiaryDetail[]</c>).
    /// Rows for the table nest under this key in the transaction UDF_Data blob.</summary>
    private static string GenerateGridKey(string? label, HashSet<string> taken)
    {
        var baseKey = "Tmx" + PascalSlug(label);
        var key = baseKey + "[]";
        var i = 2;
        while (taken.Contains(key)) key = $"{baseKey}{i++}[]";
        taken.Add(key);
        return key;
    }

    /// <summary>Subproduct/product prefix (e.g. "ILF") = the most common prefix of existing
    /// Field_Names in the (Product, Tab), so new fields follow the section convention. Falls back to "FLD".</summary>
    private static string DerivePrefix(IEnumerable<string> existingNames)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in existingNames)
        {
            var us = n.IndexOf('_');
            if (us <= 0) continue;
            var p = n[..us];
            if (!System.Text.RegularExpressions.Regex.IsMatch(p, "^[A-Za-z][A-Za-z0-9]*$")) continue;
            counts[p] = counts.TryGetValue(p, out var c) ? c + 1 : 1;
        }
        var best = counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        return (best ?? "FLD").ToUpperInvariant();
    }

    /// <summary>PascalCase slug of a label (letters/digits only), starting with a letter, ≤30 chars.</summary>
    private static string PascalSlug(string? label)
    {
        var words = System.Text.RegularExpressions.Regex
            .Replace(label ?? "", "[^A-Za-z0-9 ]+", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (s.Length == 0) s = "Field";
        if (!char.IsLetter(s[0])) s = "F" + s;
        return s.Length > 30 ? s[..30] : s;
    }

    /// <summary>Build a unique Field_Name — an explicit (validated) name, or
    /// <c>PREFIX_PascalLabel</c> following the subproduct convention — de-duped with a
    /// numeric suffix, ≤50 chars. Uniqueness is checked against <paramref name="taken"/>.</summary>
    private static string BuildFieldName(string? explicitName, string? label, int lkp, string prefix, HashSet<string> taken)
    {
        // An author-CHOSEN Field_Name must be product-wide unique — this is a hard
        // primitive, not a best-effort. A collision with any existing name in the product
        // (any sub-product; active or inactive) is REJECTED so the maker picks another;
        // we never silently rename a name the author typed. Auto-GENERATED names (below)
        // are made unique with a numeric suffix instead, since the author didn't choose them.
        if (!string.IsNullOrWhiteSpace(explicitName) && NameRx.IsMatch(explicitName.Trim()))
        {
            var chosen = explicitName.Trim();
            if (chosen.Length > 50) chosen = chosen[..50];
            if (taken.Contains(chosen))
                throw new ConflictException("field_name_duplicate",
                    $"Field name '{chosen}' is already used in this product. Field names must be unique " +
                    "across all sub-products — choose a different name.");
            return chosen;
        }

        var core = lkp == 4 ? "SubText" : PascalSlug(label);       // SubText rows carry no stored value
        var baseName = $"{prefix}_{core}";
        if (baseName.Length > 50) baseName = baseName[..50];

        var name = baseName; var i = 2;
        while (taken.Contains(name))
        {
            var suffix = "_" + i++;
            name = (baseName.Length + suffix.Length > 50 ? baseName[..(50 - suffix.Length)] : baseName) + suffix;
        }
        return name;
    }

    /// <summary>A field can be the amount field only if its type lookup resolves to a
    /// numeric/amount type. Indeterminate (no type / lookup missing) → allowed.</summary>
    private async Task<bool> IsAmountEligibleAsync(int? fieldTypeLkp, CancellationToken ct)
    {
        if (fieldTypeLkp is null) return true;
        // Field_Type_Lkp is the CODE stored in Hidden_Value of a Lookup_Type='field'
        // row (Visible_Value = "Amount"/"Text"/…), NOT the Lookup primary key. Resolve
        // it the same way the FormDefinition read side does (LoadFieldTypeNamesAsync).
        var code = fieldTypeLkp.Value.ToString();
        var visible = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "field" && l.HiddenValue == code)
            .Select(l => l.VisibleValue)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(visible)) return true;
        var t = visible.ToLowerInvariant();
        return t.Contains("amount") || t.Contains("currency") || t.Contains("money")
            || t.Contains("number") || t.Contains("numeric") || t.Contains("decimal");
    }
}
