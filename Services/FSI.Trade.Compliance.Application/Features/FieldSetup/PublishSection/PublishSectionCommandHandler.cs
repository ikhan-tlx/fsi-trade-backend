using FSI.Trade.Compliance.Application.Common.Caching;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.FieldSetup.Common;
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

        var ids = req.items.Select(i => i.tenantFieldSetupId).ToList();

        // Tracked load of every row in the batch (single round-trip).
        var entities = await _db.TenantFieldSetups
            .Where(f => ids.Contains(f.TenantFieldSetupId))
            .ToListAsync(ct);

        var byId = entities.ToDictionary(e => e.TenantFieldSetupId);

        // Every referenced row must exist AND belong to the stated product —
        // stops a caller editing fields across products by guessing ids.
        foreach (var item in req.items)
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
        foreach (var item in req.items.Where(i => i.moveParent && i.parentTenantFieldSetupId.HasValue))
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

        var now = DateTime.UtcNow;
        int updated = 0, reordered = 0, retyped = 0, moved = 0;
        // (product, tab, chosenFieldName) that were designated the amount field this batch —
        // used to enforce one-amount-field-per-sub-product across locale rows / prior state.
        var amountGroups = new List<(int? pid, int? tab, string? field)>();

        // ---- per-item edits + guardrails ----------------------------------
        foreach (var item in req.items)
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

        // ---- re-emit Field_Sequence from display order (gaps of 10) --------
        var ordered = req.items.OrderBy(i => i.order).ToList();
        for (var rank = 0; rank < ordered.Count; rank++)
        {
            var e = byId[ordered[rank].tenantFieldSetupId];
            var nextSeq = (rank + 1) * SeqGap;
            if ((e.FieldSequence ?? -1) != nextSeq)
            {
                e.FieldSequence = nextSeq;
                if (e.LastUpdatedDate != now) { e.LastUpdatedBy = userId; e.LastUpdatedDate = now; updated++; }
                reordered++;
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

        // Bust read-side form-schema caches for this product (all cultures).
        FormSchemaCacheVersion.Bump(req.productId);

        return new PublishSectionResult
        {
            productId = req.productId,
            updated   = updated,
            reordered = reordered,
            retyped   = retyped,
            moved     = moved,
        };
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
