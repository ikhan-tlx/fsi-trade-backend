using System.Globalization;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Reference;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.ReferenceLists;

// ═══════════════ Bulk upsert VALUES into a TYPE (template-driven import) ═══════
/// <summary>
/// Server-authoritative bulk import of reference VALUES into one reference TYPE
/// (POST /ReferenceList/Types/{id}/BulkValues). Each row is validated against the
/// type's domain + UDF schema; valid rows are UPSERTED (existing catalogue value
/// re-used + relinked, new codes created) and linked as members of the type.
///
/// Validation is per-row and non-fatal: a bad row is reported, the rest still
/// import. The result carries a per-row status (created | linked | updated |
/// skipped | error) + message so the FE can render an import summary — mirroring
/// the FE's client-side pre-check but authoritative (the FE check is only UX).
/// </summary>
public class BulkUpsertReferenceValuesCommand : IRequest<BulkUpsertResult>
{
    public long typeId { get; set; }
    public List<BulkValueRow> rows { get; set; } = new();

    /// <summary>
    /// Maker-time dry run. When true the handler validates every row and returns the
    /// SAME per-row summary but writes NOTHING — used by the FE before it parks the
    /// valid rows as a maker/checker request. The stored request payload has this
    /// false (default), so the approval replay actually applies the rows.
    /// </summary>
    public bool validateOnly { get; set; }
}

public class BulkValueRow
{
    /// <summary>1-based source row (for human-readable messages). 0 = unset.</summary>
    public int    rowNumber   { get; set; }
    public string code        { get; set; } = "";
    public string? name        { get; set; }
    public string? description { get; set; }
    public bool?   active       { get; set; }
    public Dictionary<string, object?> udf { get; set; } = new();
}

public class BulkRowResult
{
    public int     rowNumber { get; set; }
    public string  code      { get; set; } = "";
    /// <summary>created | linked | updated | skipped | error</summary>
    public string  status    { get; set; } = "";
    public string? message   { get; set; }
}

public class BulkUpsertResult
{
    public int total   { get; set; }
    public int created { get; set; }
    public int linked  { get; set; }
    public int updated { get; set; }
    public int skipped { get; set; }
    public int errors  { get; set; }
    public List<BulkRowResult> rows { get; set; } = new();
}

public class BulkUpsertReferenceValuesCommandHandler
    : IRequestHandler<BulkUpsertReferenceValuesCommand, BulkUpsertResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public BulkUpsertReferenceValuesCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<BulkUpsertResult> Handle(BulkUpsertReferenceValuesCommand req, CancellationToken ct)
    {
        var type = await _db.ReferenceTypes.FirstOrDefaultAsync(t => t.ReferenceTypeId == req.typeId, ct)
            ?? throw new NotFoundException("reference_type_not_found", "Reference type not found.");

        if (req.rows is null || req.rows.Count == 0)
            throw new ValidationException("rows", "The upload contains no data rows.");

        var user = _current.UserName ?? _current.UserId ?? "system";
        var now  = DateTime.UtcNow;
        var lkp  = string.IsNullOrWhiteSpace(type.LookupType) ? null : type.LookupType!.Trim();

        // UDF schema for this type (drives required / enum / number validation).
        var schema = ParseSchema(type.UdfSchema);

        // Domain integrity set: a domain-scoped type only accepts codes that are real
        // Hidden_Values in its lookup group (case-insensitive).
        HashSet<string>? validCodes = null;
        if (lkp != null)
            validCodes = (await _db.Lookups.AsNoTracking()
                    .Where(l => l.LookupType == lkp && l.HiddenValue != null)
                    .Select(l => l.HiddenValue!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Existing members of this type — used to distinguish "linked" (newly added to
        // the type) from "updated" (already a member, fields refreshed).
        var memberValueIds = (await _db.ReferenceValueTypeMaps.AsNoTracking()
                .Where(m => m.ReferenceTypeId == type.ReferenceTypeId)
                .Select(m => m.ReferenceValueId)
                .ToListAsync(ct))
            .ToHashSet();

        var result = new BulkUpsertResult { total = req.rows.Count };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Row → the resolved/created value entity (only for valid rows), kept so we can
        // link memberships after a single SaveChanges assigns ids to new values.
        var pending = new List<(BulkValueRow row, ReferenceValue value, bool isNew, bool alreadyMember)>();

        foreach (var row in req.rows)
        {
            var rn = row.rowNumber;
            var code = (row.code ?? "").Trim();

            if (code.Length == 0)
            { result.rows.Add(Err(rn, code, "Code is required.")); result.errors++; continue; }

            if (!seen.Add(code))
            { result.rows.Add(Err(rn, code, "Duplicate code within the file — first occurrence wins.")); result.skipped++; continue; }

            if (validCodes != null && !validCodes.Contains(code))
            { result.rows.Add(Err(rn, code, $"'{code}' is not a valid code in the {DomainLabels.For(lkp!)} lookup group.")); result.errors++; continue; }

            var udfError = ValidateUdf(schema, row.udf);
            if (udfError != null)
            { result.rows.Add(Err(rn, code, udfError)); result.errors++; continue; }

            // Upsert: reuse an existing catalogue value on (code, domain), else create.
            var value = await _db.ReferenceValues.FirstOrDefaultAsync(v =>
                v.Code == code && ((v.LookupType == lkp) || (v.LookupType == null && lkp == null)), ct);

            var isNew = value is null;
            var alreadyMember = !isNew && memberValueIds.Contains(value!.ReferenceValueId);

            // Maker-time dry run: predict the outcome and report it, but write NOTHING.
            if (req.validateOnly)
            {
                if (isNew)              { result.created++; result.rows.Add(Ok(rn, code, "created", "Will be created and linked.")); }
                else if (alreadyMember) { result.updated++; result.rows.Add(Ok(rn, code, "updated", "Existing member will be refreshed.")); }
                else                    { result.linked++;  result.rows.Add(Ok(rn, code, "linked",  "Existing catalogue value will be linked.")); }
                continue;
            }

            if (value is null)
            {
                value = new ReferenceValue { Code = code, LookupType = lkp, CreatedBy = user, CreatedDate = now };
                _db.ReferenceValues.Add(value);
            }
            else
            {
                value.LastUpdatedBy = user; value.LastUpdatedDate = now;
            }

            // Field refresh (name defaults to code; description/active/udf applied when supplied).
            value.Name = string.IsNullOrWhiteSpace(row.name) ? (value.Name ?? code) : row.name!.Trim();
            if (row.description != null) value.Description = row.description.Trim();
            if (row.active.HasValue)     value.ActiveFlag  = row.active.Value;
            else if (isNew)              value.ActiveFlag  = true;
            if (row.udf.Count > 0)       value.UdfData     = JsonSerializer.Serialize(row.udf);

            pending.Add((row, value, isNew, alreadyMember));
        }

        // Maker-time dry run stops here — nothing was tracked, nothing to save.
        if (!req.validateOnly)
        {
            // Assign ids to new values, then link memberships in one more save.
            await _db.SaveChangesAsync(ct);

            foreach (var (row, value, isNew, alreadyMember) in pending)
            {
                if (!alreadyMember && !memberValueIds.Contains(value.ReferenceValueId))
                {
                    _db.ReferenceValueTypeMaps.Add(new ReferenceValueTypeMap
                    {
                        ReferenceTypeId  = type.ReferenceTypeId,
                        ReferenceValueId = value.ReferenceValueId,
                        ActiveFlag       = true,
                        CreatedBy        = user,
                        CreatedDate      = now,
                    });
                    memberValueIds.Add(value.ReferenceValueId);
                }

                if (isNew)        { result.created++; result.rows.Add(Ok(row.rowNumber, value.Code, "created", "New value created and linked.")); }
                else if (alreadyMember) { result.updated++; result.rows.Add(Ok(row.rowNumber, value.Code, "updated", "Existing member refreshed.")); }
                else              { result.linked++;  result.rows.Add(Ok(row.rowNumber, value.Code, "linked",  "Existing catalogue value linked.")); }
            }

            await _db.SaveChangesAsync(ct);
        }

        result.rows.Sort((a, b) => a.rowNumber.CompareTo(b.rowNumber));
        return result;
    }

    private static BulkRowResult Err(int rn, string code, string msg) => new() { rowNumber = rn, code = code, status = "error", message = msg };
    private static BulkRowResult Ok(int rn, string code, string status, string msg) => new() { rowNumber = rn, code = code, status = status, message = msg };

    private static List<UdfFieldDto> ParseSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<UdfFieldDto>>(json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch { return new(); }
    }

    /// <summary>Per-row UDF validation against the type schema. Returns the first
    /// problem as a human message, or null when the row's UDF is acceptable.</summary>
    private static string? ValidateUdf(List<UdfFieldDto> schema, Dictionary<string, object?> udf)
    {
        foreach (var f in schema)
        {
            if (string.IsNullOrWhiteSpace(f.key)) continue;
            udf.TryGetValue(f.key, out var raw);
            var s = raw?.ToString()?.Trim() ?? "";

            if (s.Length == 0)
            {
                if (f.required) return $"'{f.label}' is required.";
                continue;
            }

            switch ((f.type ?? "string").ToLowerInvariant())
            {
                case "number":
                    if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                        return $"'{f.label}' must be a number (got '{s}').";
                    if (f.min.HasValue && num < f.min.Value) return $"'{f.label}' must be ≥ {f.min.Value}.";
                    if (f.max.HasValue && num > f.max.Value) return $"'{f.label}' must be ≤ {f.max.Value}.";
                    break;
                case "enum":
                    if (f.options is { Count: > 0 } && !f.options.Contains(s, StringComparer.OrdinalIgnoreCase))
                        return $"'{f.label}' must be one of: {string.Join(", ", f.options)}.";
                    break;
                default: // string
                    if (f.maxLength.HasValue && s.Length > f.maxLength.Value)
                        return $"'{f.label}' exceeds max length {f.maxLength.Value}.";
                    break;
            }
        }
        return null;
    }
}
