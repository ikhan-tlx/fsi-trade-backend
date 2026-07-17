using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Reference;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.ReferenceLists;

// ═══════════════ Save (create/update) TYPE ═════════════════════════════════
public class SaveReferenceTypeCommand : IRequest<long>
{
    public long?   id          { get; set; }
    public string  code        { get; set; } = "";
    public string  name        { get; set; } = "";
    public string? description { get; set; }
    public string? lookupType  { get; set; }
    public bool    active      { get; set; } = true;
    public List<UdfFieldDto> schema { get; set; } = new();
    /// <summary>Full membership set for this type (value ids + active flag).</summary>
    public List<TypeMemberInput> members { get; set; } = new();
}
public class TypeMemberInput { public long valueId { get; set; } public bool active { get; set; } = true; }

public class SaveReferenceTypeCommandHandler : IRequestHandler<SaveReferenceTypeCommand, long>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public SaveReferenceTypeCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<long> Handle(SaveReferenceTypeCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.name)) throw new ValidationException("name", "A name is required.");
        if (string.IsNullOrWhiteSpace(req.code)) throw new ValidationException("code", "A code is required.");

        var user = _current.UserName ?? _current.UserId ?? "system";
        var now = DateTime.UtcNow;
        var code = req.code.Trim().ToUpperInvariant();

        ReferenceType t;
        if (req.id is { } id && id > 0)
        {
            t = await _db.ReferenceTypes.FirstOrDefaultAsync(x => x.ReferenceTypeId == id, ct)
                ?? throw new NotFoundException("reference_type_not_found", "Reference type not found.");
            // code is immutable once any rule references it
            var used = await _db.FlagRules.AsNoTracking().AnyAsync(r => r.ActiveFlag && r.ReferenceListCode == t.TypeCode, ct);
            if (!used) t.TypeCode = code;
        }
        else
        {
            if (await _db.ReferenceTypes.AnyAsync(x => x.TypeCode == code, ct))
                throw new ConflictException("type_code_taken", $"A reference type with code '{code}' already exists.");
            t = new ReferenceType { TypeCode = code, Source = "user", CreatedBy = user, CreatedDate = now };
            _db.ReferenceTypes.Add(t);
        }

        t.TypeName   = req.name.Trim();
        t.Description = req.description?.Trim();
        t.LookupType = string.IsNullOrWhiteSpace(req.lookupType) ? null : req.lookupType.Trim();
        t.ActiveFlag = req.active;
        t.UdfSchema  = JsonSerializer.Serialize(req.schema.Where(f => !string.IsNullOrWhiteSpace(f.key)));
        if (t.ReferenceTypeId != 0) { t.LastUpdatedBy = user; t.LastUpdatedDate = now; }

        await _db.SaveChangesAsync(ct);   // ensure ReferenceTypeId

        // Replace memberships for this type.
        var existing = await _db.ReferenceValueTypeMaps.Where(m => m.ReferenceTypeId == t.ReferenceTypeId).ToListAsync(ct);
        var wantIds = req.members.Select(m => m.valueId).ToHashSet();
        foreach (var m in existing.Where(m => !wantIds.Contains(m.ReferenceValueId)))
            _db.ReferenceValueTypeMaps.Remove(m);
        foreach (var mi in req.members)
        {
            var row = existing.FirstOrDefault(m => m.ReferenceValueId == mi.valueId);
            if (row is null)
                _db.ReferenceValueTypeMaps.Add(new ReferenceValueTypeMap
                { ReferenceTypeId = t.ReferenceTypeId, ReferenceValueId = mi.valueId, ActiveFlag = mi.active, CreatedBy = user, CreatedDate = now });
            else row.ActiveFlag = mi.active;
        }
        await _db.SaveChangesAsync(ct);
        return t.ReferenceTypeId;
    }
}

// ═══════════════ Delete TYPE ════════════════════════════════════════════════
public record DeleteReferenceTypeCommand(long Id) : IRequest<bool>;

public class DeleteReferenceTypeCommandHandler : IRequestHandler<DeleteReferenceTypeCommand, bool>
{
    private readonly IApplicationDbContext _db;
    public DeleteReferenceTypeCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteReferenceTypeCommand req, CancellationToken ct)
    {
        var t = await _db.ReferenceTypes.FirstOrDefaultAsync(x => x.ReferenceTypeId == req.Id, ct)
            ?? throw new NotFoundException("reference_type_not_found", "Reference type not found.");
        var used = await _db.FlagRules.AsNoTracking().CountAsync(r => r.ActiveFlag && r.ReferenceListCode == t.TypeCode, ct);
        if (used > 0)
            throw new ConflictException("type_in_use", $"Cannot delete — {used} rule(s) reference this type.");

        var maps = await _db.ReferenceValueTypeMaps.Where(m => m.ReferenceTypeId == t.ReferenceTypeId).ToListAsync(ct);
        _db.ReferenceValueTypeMaps.RemoveRange(maps);
        _db.ReferenceTypes.Remove(t);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ═══════════════ Save (create/update) VALUE ════════════════════════════════
public class SaveReferenceValueCommand : IRequest<long>
{
    public long?   id          { get; set; }
    public string  code        { get; set; } = "";
    public string? name        { get; set; }
    public string? description { get; set; }
    public string? lookupType  { get; set; }
    public bool    active      { get; set; } = true;
    public Dictionary<string, object?> udf { get; set; } = new();
    public List<long> typeIds { get; set; } = new();
}

public class SaveReferenceValueCommandHandler : IRequestHandler<SaveReferenceValueCommand, long>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public SaveReferenceValueCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<long> Handle(SaveReferenceValueCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.code)) throw new ValidationException("code", "Pick a code from the lookup.");

        var user = _current.UserName ?? _current.UserId ?? "system";
        var now = DateTime.UtcNow;
        var code = req.code.Trim();
        var lkp = string.IsNullOrWhiteSpace(req.lookupType) ? null : req.lookupType.Trim();

        ReferenceValue v;
        if (req.id is { } id && id > 0)
        {
            v = await _db.ReferenceValues.FirstOrDefaultAsync(x => x.ReferenceValueId == id, ct)
                ?? throw new NotFoundException("reference_value_not_found", "Reference value not found.");
        }
        else
        {
            var dup = await _db.ReferenceValues.AnyAsync(x => x.Code == code &&
                ((x.LookupType == lkp) || (x.LookupType == null && lkp == null)), ct);
            if (dup) throw new ConflictException("value_exists", "This value already exists in the catalogue.");
            v = new ReferenceValue { Code = code, LookupType = lkp, CreatedBy = user, CreatedDate = now };
            _db.ReferenceValues.Add(v);
        }

        // integrity: a domain-scoped code must be a real Hidden_Value in its group.
        if (lkp != null)
        {
            var ok = await _db.Lookups.AsNoTracking().AnyAsync(l => l.LookupType == lkp && l.HiddenValue == code, ct);
            if (!ok) throw new ValidationException("code", $"'{code}' is not a valid value in the {DomainLabels.For(lkp)} lookup group.");
        }

        v.Name = string.IsNullOrWhiteSpace(req.name) ? code : req.name.Trim();
        v.Description = req.description?.Trim();
        v.ActiveFlag = req.active;
        v.UdfData = req.udf.Count == 0 ? null : JsonSerializer.Serialize(req.udf);
        if (v.ReferenceValueId != 0) { v.LastUpdatedBy = user; v.LastUpdatedDate = now; }

        await _db.SaveChangesAsync(ct);

        // Replace memberships for this value.
        var existing = await _db.ReferenceValueTypeMaps.Where(m => m.ReferenceValueId == v.ReferenceValueId).ToListAsync(ct);
        var want = req.typeIds.ToHashSet();
        foreach (var m in existing.Where(m => !want.Contains(m.ReferenceTypeId)))
            _db.ReferenceValueTypeMaps.Remove(m);
        foreach (var tid in want.Where(tid => existing.All(m => m.ReferenceTypeId != tid)))
            _db.ReferenceValueTypeMaps.Add(new ReferenceValueTypeMap
            { ReferenceTypeId = tid, ReferenceValueId = v.ReferenceValueId, ActiveFlag = true, CreatedBy = user, CreatedDate = now });
        await _db.SaveChangesAsync(ct);
        return v.ReferenceValueId;
    }
}

// ═══════════════ Delete VALUE ═══════════════════════════════════════════════
public record DeleteReferenceValueCommand(long Id) : IRequest<bool>;

public class DeleteReferenceValueCommandHandler : IRequestHandler<DeleteReferenceValueCommand, bool>
{
    private readonly IApplicationDbContext _db;
    public DeleteReferenceValueCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteReferenceValueCommand req, CancellationToken ct)
    {
        var v = await _db.ReferenceValues.FirstOrDefaultAsync(x => x.ReferenceValueId == req.Id, ct)
            ?? throw new NotFoundException("reference_value_not_found", "Reference value not found.");
        var mapped = await _db.ReferenceValueTypeMaps.CountAsync(m => m.ReferenceValueId == v.ReferenceValueId, ct);
        if (mapped > 0)
            throw new ConflictException("value_in_use", $"Cannot delete — value is mapped to {mapped} categor{(mapped > 1 ? "ies" : "y")}.");
        _db.ReferenceValues.Remove(v);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
