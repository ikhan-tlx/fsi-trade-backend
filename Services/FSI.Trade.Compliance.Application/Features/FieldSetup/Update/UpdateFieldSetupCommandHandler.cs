using FSI.Trade.Compliance.Application.Common.Caching;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.FieldSetup.Update;

/// <summary>
/// Applies the admin-page edit to one TmX_Tenant_Field_Setup row.
///
/// Three behavioural details worth knowing about:
/// <list type="bullet">
///   <item><b>Formula detection on isMandatory</b> — we only overwrite
///         the column when the existing value is null / empty / literal
///         <c>"true"</c> / literal <c>"false"</c>. A formula like
///         <c>"tf274526 == true"</c> is treated as platform-managed and
///         the request is rejected with 409.</item>
///   <item><b>Cache invalidation</b> — after a successful save we
///         <c>FormSchemaCacheVersion.Bump(productId)</c>, which
///         invalidates every cached FormDefinition / Tabs / TabFields
///         entry for that product across every culture variant.</item>
///   <item><b>Audit stamps</b> — Last_Updated_By + Last_Updated_Date are
///         set on every successful write so admin edits are traceable.</item>
/// </list>
/// </summary>
public class UpdateFieldSetupCommandHandler
    : IRequestHandler<UpdateFieldSetupCommand, UpdateFieldSetupResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateFieldSetupCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<UpdateFieldSetupResult> Handle(UpdateFieldSetupCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
            ?? throw new AuthenticationException(
                "unauthenticated",
                "FieldSetup edits require an authenticated caller.");

        // Tracked load — we're going to mutate.
        var entity = await _db.TenantFieldSetups
            .FirstOrDefaultAsync(f => f.TenantFieldSetupId == req.tenantFieldSetupId, ct)
            ?? throw new NotFoundException(
                "field_setup_not_found",
                $"TenantFieldSetup {req.tenantFieldSetupId} does not exist.");

        // ---- fieldLabel ------------------------------------------------
        if (req.fieldLabel is not null)
            entity.FieldLabel = req.fieldLabel.Trim();

        // ---- placeholder (empty string clears) -------------------------
        if (req.placeholder is not null)
            entity.Placeholder = req.placeholder.Length == 0
                ? null
                : req.placeholder.Trim();

        // ---- isActive --------------------------------------------------
        if (req.isActive.HasValue)
            entity.IsActive = req.isActive.Value;

        // ---- isMandatory (formula detection) ---------------------------
        if (req.isMandatory.HasValue)
        {
            if (!IsSafeToOverwrite(entity.IsMandatory))
            {
                throw new ConflictException(
                    "field_has_conditional_rule",
                    $"Field '{entity.FieldName}' has a conditional Is_Mandatory rule " +
                    $"({Truncate(entity.IsMandatory, 80)}). The admin toggle can only " +
                    "overwrite literal true/false values. Contact the platform team to " +
                    "modify the rule.");
            }

            entity.IsMandatory = req.isMandatory.Value ? "true" : "false";
        }

        // ---- isAmountField (per-sub-product radio; mutual exclusivity) ----
        if (req.isAmountField.HasValue)
        {
            if (req.isAmountField.Value && !await IsAmountEligibleAsync(entity.FieldTypeLkp, ct))
            {
                throw new ConflictException(
                    "field_not_amount_type",
                    $"Field '{entity.FieldName}' is not a numeric/amount field, so it can't be " +
                    "designated the auto-resolved amount field.");
            }

            // All rows in this (Product, Tab) sub-product — includes the field's locale rows.
            var group = await _db.TenantFieldSetups
                .Where(f => f.ProductId == entity.ProductId
                            && (entity.TabId == null ? f.TabId == null : f.TabId == entity.TabId))
                .ToListAsync(ct);

            foreach (var f in group)
            {
                if (string.Equals(f.FieldName, entity.FieldName, StringComparison.OrdinalIgnoreCase))
                    f.IsAmountField = req.isAmountField.Value;          // set/clear this field (all its locale rows)
                else if (req.isAmountField.Value)
                    f.IsAmountField = false;                           // radio: clear every other field
            }
        }

        // ---- audit stamps ----------------------------------------------
        entity.LastUpdatedBy   = userId;
        entity.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Invalidate read-side caches for this product. Note: TabId can
        // legitimately be null on root-level fields; cache bump is per-
        // product regardless, which covers both tab-scoped and tab-less
        // fields in one go.
        if (entity.ProductId.HasValue)
            FormSchemaCacheVersion.Bump(entity.ProductId.Value);

        return new UpdateFieldSetupResult
        {
            tenantFieldSetupId = entity.TenantFieldSetupId,
            productId          = entity.ProductId ?? 0,
            tabId              = entity.TabId,
            fieldName          = entity.FieldName,
            fieldLabel         = entity.FieldLabel,
            placeholder        = entity.Placeholder,
            isActive           = entity.IsActive,
            isMandatory        = entity.IsMandatory,
            isAmountField      = entity.IsAmountField
        };
    }

    /// <summary>A field can be the amount field only if its type lookup resolves to a
    /// numeric/amount type. Indeterminate (no type / lookup missing) → allowed (the FE
    /// already restricts the toggle to numeric fields).</summary>
    private async Task<bool> IsAmountEligibleAsync(int? fieldTypeLkp, CancellationToken ct)
    {
        if (fieldTypeLkp is null) return true;
        // Field_Type_Lkp is the CODE stored in Hidden_Value of a Lookup_Type='field'
        // row (Visible_Value = "Amount"/"Text"/…), NOT the Lookup primary key. Resolve
        // it exactly the way the FormDefinition read side does (LoadFieldTypeNamesAsync).
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

    /// <summary>
    /// True when the existing Is_Mandatory value is null / empty / one
    /// of the literal booleans. False when a formula is present —
    /// indicates the field is platform-managed and the toggle should
    /// reject rather than clobber the rule.
    /// </summary>
    private static bool IsSafeToOverwrite(string? existing)
    {
        if (string.IsNullOrWhiteSpace(existing)) return true;
        var trimmed = existing.Trim();
        return string.Equals(trimmed, "true",  StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)
            // Some legacy rows stored "1" / "0" — same intent as literal bool.
            || trimmed == "1"
            || trimmed == "0";
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
