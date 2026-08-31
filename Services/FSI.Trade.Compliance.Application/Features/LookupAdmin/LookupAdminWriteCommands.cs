using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.LookupAdmin;

// ═══════════════ Shared validation (enqueue-time AND apply-time) ═════════════
internal static class LookupValueValidator
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };
    public static readonly  JsonSerializerOptions WriteOpts = new();   // default = PascalCase preserved

    public static string Serialize(LookupChangeIntent intent) => JsonSerializer.Serialize(intent, WriteOpts);

    public static LookupChangeIntent Deserialize(string? json)
        => (string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LookupChangeIntent>(json!, ReadOpts))
           ?? throw new ConflictException("lookup_request_payload_invalid", "The lookup change payload is missing or invalid.");

    /// <summary>Validate a create/update intent. Throws the plan §5 coded exception on failure.</summary>
    public static async Task ValidateSaveAsync(IApplicationDbContext db, LookupChangeIntent intent, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(db, intent.LookupType, ct);

        if (string.IsNullOrWhiteSpace(intent.Label) || string.IsNullOrWhiteSpace(intent.Code))
            throw new ValidationException("lookup_value_required_field", "A label and a code are required.");

        var lt   = intent.LookupType.Trim();
        var code = intent.Code!.Trim();

        if (intent.LookupId is { } id && id > 0)
        {
            var existing = await db.Lookups.AsNoTracking()
                .Where(l => l.Id == id).Select(l => new { l.Id, l.LookupType, l.UserEditable }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");
            if (existing.UserEditable == false)   // NULL/true = editable; only explicit 0 locks
                throw new ForbiddenException("lookup_value_not_editable", "This value is locked (User_Editable = 0) and cannot be edited.");
        }

        // Unique Hidden_Value within (Lookup_Type, ParentLookupId), excluding self.
        var dupExists = await db.Lookups.AsNoTracking().AnyAsync(l =>
            l.LookupType == lt &&
            l.HiddenValue == code &&
            l.ParentLookupId == intent.ParentId &&
            (intent.LookupId == null || l.Id != intent.LookupId), ct);
        if (dupExists)
            throw new ConflictException("lookup_value_duplicate_code", $"A value with code '{code}' already exists in this type/parent.");
    }

    // IMPORTANT: the validators below are PROJECTION-ONLY and never materialise a
    // full Lookup entity. TmX_Lookup is legacy reference data where some rows carry
    // NULLs in columns mapped to non-nullable CLR properties (e.g. Active_Flag,
    // Tenant_ID); a full-entity load (db.Lookups.FirstOrDefaultAsync(l => ...))
    // throws SqlNullValueException ("Data is Null") when it hits one — which is why
    // the whole read side already projects. The apply handler mutates via
    // ExecuteUpdate/ExecuteDelete (below) for the same reason.

    /// <summary>Validate an activate/deactivate intent (manageable, exists, editable).</summary>
    public static async Task ValidateActiveAsync(IApplicationDbContext db, LookupChangeIntent intent, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(db, intent.LookupType, ct);
        var row = await db.Lookups.AsNoTracking()
            .Where(l => l.Id == intent.LookupId)
            .Select(l => new { l.Id, l.UserEditable })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");
        if (row.UserEditable == false)   // NULL/true = editable; only explicit 0 locks
            throw new ForbiddenException("lookup_value_not_editable", "This value is locked (User_Editable = 0) and cannot be changed.");
    }

    /// <summary>Validate a delete intent (manageable, exists, editable, not in use).</summary>
    public static async Task ValidateDeleteAsync(IApplicationDbContext db, LookupChangeIntent intent, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(db, intent.LookupType, ct);
        var row = await db.Lookups.AsNoTracking()
            .Where(l => l.Id == intent.LookupId)
            .Select(l => new { l.Id, l.LookupType, l.HiddenValue, l.UserEditable })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");
        if (row.UserEditable == false)   // NULL/true = editable; only explicit 0 locks
            throw new ForbiddenException("lookup_value_not_editable", "This value is locked (User_Editable = 0) and cannot be deleted.");
        var usage = await LookupValueUsageComputer.ComputeAsync(db, row.LookupType!, row.HiddenValue, ct);
        if (usage.inUse)
            throw new ConflictException("lookup_value_in_use",
                $"Cannot delete — value is referenced by {usage.ruleUsageCount} rule(s) and {usage.txnUsageCount} transaction(s). Deactivate instead.");
    }

    /// <summary>Create the pending TmX_Request row for a validated intent and return its id.</summary>
    public static async Task<long> EnqueueAsync(
        IApplicationDbContext db, ICurrentUserService current, LookupChangeIntent intent, string entityStatusCode, CancellationToken ct)
    {
        var req = new Request
        {
            TenantId          = current.TenantId ?? 1,
            RequestTypeCode   = LookupRequestCodes.RequestType_Lookup,
            EntityStatusCode  = entityStatusCode,
            RequestStatusCode = LookupRequestCodes.RequestStatus_Pending,
            EntityId          = intent.LookupId?.ToString() ?? "",
            EntityJson        = Serialize(intent),
            CreatedBy         = current.UserName ?? current.UserId ?? "system",
            CreatedDate       = DateTime.UtcNow,
        };
        db.Requests.Add(req);
        await db.SaveChangesAsync(ct);
        return req.RequestId;
    }
}

// ═══════════════ Save value (create/update) → enqueue request ════════════════
public class SaveLookupValueCommand : IRequest<long>
{
    public int?     id          { get; set; }
    public string   lookupType  { get; set; } = "";
    public string?  label       { get; set; }
    public string?  code        { get; set; }
    public string?  description { get; set; }
    public int?     parentId    { get; set; }
    public int?     sortOrder   { get; set; }
    public bool     isActive    { get; set; } = true;
}

public class SaveLookupValueCommandHandler : IRequestHandler<SaveLookupValueCommand, long>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public SaveLookupValueCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<long> Handle(SaveLookupValueCommand req, CancellationToken ct)
    {
        var isUpdate = req.id is { } id && id > 0;
        var intent = new LookupChangeIntent
        {
            Operation   = isUpdate ? "update" : "create",
            LookupType  = req.lookupType?.Trim() ?? "",
            LookupId    = isUpdate ? req.id : null,
            Label       = req.label?.Trim(),
            Code        = req.code?.Trim(),
            Description = req.description?.Trim(),
            ParentId    = req.parentId,
            SortOrder   = req.sortOrder,
            IsActive    = req.isActive,
            Name        = $"{req.lookupType} / {req.code}",
        };

        await LookupValueValidator.ValidateSaveAsync(_db, intent, ct);   // maker-time validation
        var status = isUpdate ? LookupRequestCodes.EntityStatus_Update : LookupRequestCodes.EntityStatus_Create;
        return await LookupValueValidator.EnqueueAsync(_db, _current, intent, status, ct);
    }
}

// ═══════════════ Activate / deactivate → enqueue request ═════════════════════
public class SetLookupValueActiveCommand : IRequest<long>
{
    public int  id       { get; set; }
    public bool isActive { get; set; }
}

public class SetLookupValueActiveCommandHandler : IRequestHandler<SetLookupValueActiveCommand, long>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public SetLookupValueActiveCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<long> Handle(SetLookupValueActiveCommand req, CancellationToken ct)
    {
        // Resolve the row's type first so the guard + intent are populated.
        var row = await _db.Lookups.AsNoTracking()
            .Where(l => l.Id == req.id).Select(l => new { l.LookupType, l.VisibleValue, l.HiddenValue }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");

        var intent = new LookupChangeIntent
        {
            Operation  = req.isActive ? "activate" : "deactivate",
            LookupType = row.LookupType ?? "",
            LookupId   = req.id,
            IsActive   = req.isActive,
            Code       = row.HiddenValue,
            Label      = row.VisibleValue,
            Name       = $"{row.LookupType} / {row.HiddenValue}",
        };

        await LookupValueValidator.ValidateActiveAsync(_db, intent, ct);
        var status = req.isActive ? LookupRequestCodes.EntityStatus_Update : LookupRequestCodes.EntityStatus_Deactivate;
        return await LookupValueValidator.EnqueueAsync(_db, _current, intent, status, ct);
    }
}

// ═══════════════ Delete → enqueue request ════════════════════════════════════
public record DeleteLookupValueCommand(int Id) : IRequest<long>;

public class DeleteLookupValueCommandHandler : IRequestHandler<DeleteLookupValueCommand, long>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public DeleteLookupValueCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<long> Handle(DeleteLookupValueCommand req, CancellationToken ct)
    {
        var row = await _db.Lookups.AsNoTracking()
            .Where(l => l.Id == req.Id).Select(l => new { l.LookupType, l.VisibleValue, l.HiddenValue }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");

        var intent = new LookupChangeIntent
        {
            Operation  = "delete",
            LookupType = row.LookupType ?? "",
            LookupId   = req.Id,
            Code       = row.HiddenValue,
            Label      = row.VisibleValue,
            Name       = $"{row.LookupType} / {row.HiddenValue}",
        };

        await LookupValueValidator.ValidateDeleteAsync(_db, intent, ct);   // maker-time not-in-use check
        return await LookupValueValidator.EnqueueAsync(_db, _current, intent, LookupRequestCodes.EntityStatus_Delete, ct);
    }
}

// ═══════════════ Bulk value import → enqueue ONE request ═════════════════════
public class LookupBulkRow
{
    /// <summary>1-based source row, for human-readable messages.</summary>
    public int     rowNumber   { get; set; }
    public string  code        { get; set; } = "";
    public string? label       { get; set; }
    public string? description { get; set; }
    public bool?   active      { get; set; }
}

public class LookupBulkRowResult
{
    public int     rowNumber { get; set; }
    public string  code      { get; set; } = "";
    /// <summary>create | update | skipped | error</summary>
    public string  status    { get; set; } = "";
    public string? message   { get; set; }
}

public class LookupBulkResult
{
    /// <summary>The single pending request that carries all valid rows.</summary>
    public long requestId { get; set; }
    public int  total     { get; set; }
    public int  create    { get; set; }
    public int  update    { get; set; }
    public int  skipped   { get; set; }
    public int  errors    { get; set; }
    public List<LookupBulkRowResult> rows { get; set; } = new();
}

/// <summary>
/// Template-driven bulk value import for a managed lookup type. Validates every
/// row at maker time (manageable guard + code required + in-file de-dupe), then
/// parks the VALID rows as ONE pending Lookup Modification Request (Operation
/// "bulk") — invalid rows are reported back and dropped. On approval the whole
/// batch is applied atomically by <see cref="ApplyLookupChangeCommand"/>. Mirrors
/// the per-value maker/checker flow so the approval queue shows one row per file.
/// </summary>
public class BulkSubmitLookupValuesCommand : IRequest<LookupBulkResult>
{
    public string lookupType { get; set; } = "";
    public int?   parentId   { get; set; }
    public List<LookupBulkRow> rows { get; set; } = new();
}

public class BulkSubmitLookupValuesCommandHandler : IRequestHandler<BulkSubmitLookupValuesCommand, LookupBulkResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public BulkSubmitLookupValuesCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<LookupBulkResult> Handle(BulkSubmitLookupValuesCommand req, CancellationToken ct)
    {
        await LookupAdminGuard.EnsureManageableAsync(_db, req.lookupType, ct);   // maker-time guard
        if (req.rows is null || req.rows.Count == 0)
            throw new ValidationException("rows", "The upload contains no data rows.");

        var lt = req.lookupType.Trim();

        // Existing codes in this (type, parent) → distinguishes create vs update.
        var existingCodes = (await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == lt && l.ParentLookupId == req.parentId && l.HiddenValue != null)
                .Select(l => l.HiddenValue!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new LookupBulkResult { total = req.rows.Count };
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid  = new List<LookupBulkRowIntent>();

        foreach (var row in req.rows)
        {
            var rn   = row.rowNumber;
            var code = (row.code ?? "").Trim();

            if (code.Length == 0)
            { result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "error", message = "Code is required." }); result.errors++; continue; }

            if (!seen.Add(code))
            { result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "skipped", message = "Duplicate code within the file — first occurrence wins." }); result.skipped++; continue; }

            var label = string.IsNullOrWhiteSpace(row.label) ? code : row.label!.Trim();
            valid.Add(new LookupBulkRowIntent { Code = code, Label = label, Description = row.description?.Trim(), IsActive = row.active ?? true });

            if (existingCodes.Contains(code))
            { result.update++; result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "update", message = "Existing value will be updated." }); }
            else
            { result.create++; result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "create", message = "New value will be created." }); }
        }

        if (valid.Count == 0)
            throw new ValidationException("rows", "No valid rows to import — every row was rejected. Fix the file and re-upload.");

        var intent = new LookupChangeIntent
        {
            Operation  = "bulk",
            LookupType = lt,
            ParentId   = req.parentId,
            Rows       = valid,
            Name       = $"{lt}: bulk import ({valid.Count} value{(valid.Count == 1 ? "" : "s")})",
        };
        result.requestId = await LookupValueValidator.EnqueueAsync(_db, _current, intent, LookupRequestCodes.EntityStatus_Create, ct);

        result.rows.Sort((a, b) => a.rowNumber.CompareTo(b.rowNumber));
        return result;
    }
}

// ═══════════════ Create a NEW lookup type (+ seed values) → enqueue ══════════
/// <summary>
/// Create a brand-new managed lookup type from Lookup Management, seeded with one
/// or more values (entered manually OR uploaded/bulk — same row shape as the value
/// bulk import). The type name must not already exist (compiled allow-list, the
/// TmX_Lookup_Type registry, or any TmX_Lookup row). Parks ONE pending Lookup
/// Modification Request (Operation "createType"); on approval the type is registered
/// as manageable (auto-allow) and its seed values inserted.
/// </summary>
public class SaveLookupTypeCommand : IRequest<LookupBulkResult>
{
    public string lookupType { get; set; } = "";
    public List<LookupBulkRow> rows { get; set; } = new();
}

public class SaveLookupTypeCommandHandler : IRequestHandler<SaveLookupTypeCommand, LookupBulkResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public SaveLookupTypeCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<LookupBulkResult> Handle(SaveLookupTypeCommand req, CancellationToken ct)
    {
        var lt = (req.lookupType ?? "").Trim();
        if (lt.Length == 0)
            throw new ValidationException("lookup_type_required", "A lookup type name is required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(lt, "^[A-Za-z0-9_]{2,100}$"))
            throw new ValidationException("lookup_type_invalid",
                "The type name must be 2–100 characters — letters, digits and underscore only (no spaces).");

        // Must not already exist anywhere.
        if (LookupAdminAllowList.Contains(lt)
            || await _db.LookupTypeRegistrations.AsNoTracking().AnyAsync(r => r.LookupType == lt, ct)
            || await _db.Lookups.AsNoTracking().AnyAsync(l => l.LookupType == lt, ct))
            throw new ConflictException("lookup_type_exists", $"The lookup type '{lt}' already exists.");

        if (req.rows is null || req.rows.Count == 0)
            throw new ValidationException("rows", "Add at least one value (manually or via upload) for the new type.");

        // Validate + de-dupe the seed rows (mirrors the value bulk import).
        var result = new LookupBulkResult { total = req.rows.Count };
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid  = new List<LookupBulkRowIntent>();
        foreach (var row in req.rows)
        {
            var rn   = row.rowNumber;
            var code = (row.code ?? "").Trim();
            if (code.Length == 0)
            { result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "error", message = "Code is required." }); result.errors++; continue; }
            if (!seen.Add(code))
            { result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "skipped", message = "Duplicate code within the file — first occurrence wins." }); result.skipped++; continue; }
            var label = string.IsNullOrWhiteSpace(row.label) ? code : row.label!.Trim();
            valid.Add(new LookupBulkRowIntent { Code = code, Label = label, Description = row.description?.Trim(), IsActive = row.active ?? true });
            result.create++;
            result.rows.Add(new LookupBulkRowResult { rowNumber = rn, code = code, status = "create", message = "New value will be created." });
        }
        if (valid.Count == 0)
            throw new ValidationException("rows", "No valid rows — every value row was rejected. Fix and retry.");

        var intent = new LookupChangeIntent
        {
            Operation  = "createType",
            LookupType = lt,
            Rows       = valid,
            Name       = $"New lookup type: {lt} ({valid.Count} value{(valid.Count == 1 ? "" : "s")})",
        };
        result.requestId = await LookupValueValidator.EnqueueAsync(_db, _current, intent, LookupRequestCodes.EntityStatus_Create, ct);
        result.rows.Sort((a, b) => a.rowNumber.CompareTo(b.rowNumber));
        return result;
    }
}

// ═══════════════ Apply on approval (called by SubmitRequestCommand) ══════════
/// <summary>
/// Applies an approved Lookup Modification Request to <c>TmX_Lookup</c>. Invoked
/// from <c>SubmitRequestCommandHandler</c> only when a request of type "0002" is
/// approved (checker decision "0012"). Re-runs the SAME validation before
/// committing (plan §5, §6) so a stale/now-conflicting request fails at apply
/// time and the request stays pending for the checker.
/// </summary>
public record ApplyLookupChangeCommand(string? EntityJson) : IRequest<int>;

public class ApplyLookupChangeCommandHandler : IRequestHandler<ApplyLookupChangeCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public ApplyLookupChangeCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<int> Handle(ApplyLookupChangeCommand req, CancellationToken ct)
    {
        var intent = LookupValueValidator.Deserialize(req.EntityJson);
        var who    = _current.UserName ?? _current.UserId ?? "system";
        var op     = (intent.Operation ?? "").Trim().ToLowerInvariant();

        switch (op)
        {
            case "create":
            {
                await LookupValueValidator.ValidateSaveAsync(_db, intent, ct);
                var row = new Lookup
                {
                    LookupType     = intent.LookupType.Trim(),
                    LookupName     = intent.Label?.Trim(),
                    VisibleValue   = intent.Label?.Trim(),
                    HiddenValue    = intent.Code?.Trim(),
                    Description    = intent.Description?.Trim(),
                    ParentLookupId = intent.ParentId,
                    SortOrder      = intent.SortOrder,
                    IsActive       = intent.IsActive,
                    ActiveFlag     = true,          // legacy live flag — module governance uses Is_Active (plan §4.1)
                    UserEditable   = true,          // module-created values remain editable
                    CreatedBy      = who,           // Created_By is NOT NULL in TmX_Lookup
                    LastUpdatedBy  = who,
                    LocaleId       = 1,             // Locale_ID is NOT NULL; single-locale default (plan §4.8)
                    // TenantId defaults to 1 (single-tenant — plan §4.5); Created_Date/Last_Updated_Date
                    // are unmapped (legacy nvarchar landmine — see LookupConfiguration) and left to the DB.
                };
                _db.Lookups.Add(row);
                await _db.SaveChangesAsync(ct);
                return row.Id;
            }
            case "update":
            {
                await LookupValueValidator.ValidateSaveAsync(_db, intent, ct);
                // ExecuteUpdate: set-based UPDATE by key, never materialising the full
                // Lookup entity (avoids the legacy NULL-column landmine — see the
                // validators — and the snapshot pitfalls of an attach+mutate stub).
                var n = await _db.Lookups.Where(l => l.Id == intent.LookupId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.VisibleValue,   intent.Label!.Trim())
                        .SetProperty(l => l.HiddenValue,    intent.Code!.Trim())
                        .SetProperty(l => l.Description,    intent.Description != null ? intent.Description.Trim() : null)
                        .SetProperty(l => l.ParentLookupId, intent.ParentId)
                        .SetProperty(l => l.SortOrder,      intent.SortOrder)
                        .SetProperty(l => l.IsActive,       intent.IsActive)
                        .SetProperty(l => l.LastUpdatedBy,  who), ct);
                if (n == 0) throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");
                return intent.LookupId ?? 0;
            }
            case "activate":
            case "deactivate":
            {
                await LookupValueValidator.ValidateActiveAsync(_db, intent, ct);
                var n = await _db.Lookups.Where(l => l.Id == intent.LookupId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.IsActive,      intent.IsActive)
                        .SetProperty(l => l.LastUpdatedBy, who), ct);
                if (n == 0) throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");
                return intent.LookupId ?? 0;
            }
            case "delete":
            {
                await LookupValueValidator.ValidateDeleteAsync(_db, intent, ct);
                var n = await _db.Lookups.Where(l => l.Id == intent.LookupId).ExecuteDeleteAsync(ct);
                if (n == 0) throw new NotFoundException("lookup_value_not_found", "Lookup value not found.");
                return intent.LookupId ?? 0;
            }
            case "bulk":
            {
                // Template-driven bulk import: upsert each valid row on (type, parent, code).
                // Re-checks manageability at apply time (a type could have been un-bound
                // since the maker submitted). Rows were validated + de-duped at upload.
                await LookupAdminGuard.EnsureManageableAsync(_db, intent.LookupType, ct);
                var lt   = intent.LookupType.Trim();
                var rows = intent.Rows ?? new List<LookupBulkRowIntent>();
                var applied = 0;
                foreach (var r in rows)
                {
                    var code = (r.Code ?? "").Trim();
                    if (code.Length == 0) continue;
                    var label = string.IsNullOrWhiteSpace(r.Label) ? code : r.Label!.Trim();

                    var existingId = await _db.Lookups.AsNoTracking()
                        .Where(l => l.LookupType == lt && l.HiddenValue == code && l.ParentLookupId == intent.ParentId)
                        .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);

                    if (existingId is { } eid)
                    {
                        await _db.Lookups.Where(l => l.Id == eid).ExecuteUpdateAsync(s => s
                            .SetProperty(l => l.VisibleValue,  label)
                            .SetProperty(l => l.Description,    r.Description != null ? r.Description.Trim() : null)
                            .SetProperty(l => l.IsActive,       r.IsActive)
                            .SetProperty(l => l.LastUpdatedBy,  who), ct);
                    }
                    else
                    {
                        _db.Lookups.Add(new Lookup
                        {
                            LookupType     = lt,
                            LookupName     = label,
                            VisibleValue   = label,
                            HiddenValue    = code,
                            Description    = r.Description?.Trim(),
                            ParentLookupId = intent.ParentId,
                            IsActive       = r.IsActive,
                            ActiveFlag     = true,
                            UserEditable   = true,
                            CreatedBy      = who,
                            LastUpdatedBy  = who,
                            LocaleId       = 1,
                        });
                    }
                    applied++;
                }
                await _db.SaveChangesAsync(ct);
                return applied;
            }
            case "createtype":
            {
                var lt = intent.LookupType.Trim();
                // Stale/idempotency guard — the type must still not exist.
                if (LookupAdminAllowList.Contains(lt)
                    || await _db.LookupTypeRegistrations.AsNoTracking().AnyAsync(r => r.LookupType == lt, ct)
                    || await _db.Lookups.AsNoTracking().AnyAsync(l => l.LookupType == lt, ct))
                    throw new ConflictException("lookup_type_exists", $"The lookup type '{lt}' already exists.");

                // Register the type as manageable (auto-allow) …
                _db.LookupTypeRegistrations.Add(new LookupTypeRegistration
                {
                    LookupType   = lt,
                    IsManageable = true,
                    IsSystem     = false,
                    CreatedBy    = who,
                    CreatedDate  = DateTime.UtcNow,
                });

                // … and insert its seed values.
                var newRows = intent.Rows ?? new List<LookupBulkRowIntent>();
                var created = 0;
                foreach (var r in newRows)
                {
                    var code = (r.Code ?? "").Trim();
                    if (code.Length == 0) continue;
                    var label = string.IsNullOrWhiteSpace(r.Label) ? code : r.Label!.Trim();
                    _db.Lookups.Add(new Lookup
                    {
                        LookupType     = lt,
                        LookupName     = label,
                        VisibleValue   = label,
                        HiddenValue    = code,
                        Description    = r.Description?.Trim(),
                        IsActive       = r.IsActive,
                        ActiveFlag     = true,
                        UserEditable   = true,
                        CreatedBy      = who,
                        LastUpdatedBy  = who,
                        LocaleId       = 1,
                    });
                    created++;
                }
                await _db.SaveChangesAsync(ct);
                return created;
            }
            default:
                throw new ConflictException("lookup_request_operation_invalid", $"Unknown lookup operation '{intent.Operation}'.");
        }
    }
}
