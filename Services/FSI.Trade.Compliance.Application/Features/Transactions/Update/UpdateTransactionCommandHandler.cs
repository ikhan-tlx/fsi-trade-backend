using System.Text.Json;
using System.Text.Json.Nodes;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Flags;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Flags.Write;
using FSI.Trade.Compliance.Application.Features.Transactions.Common;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using FSI.Trade.Compliance.Domain.Entities.Customer;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Update;

/// <summary>
/// Saves the current state of an existing transaction. Mirrors legacy
/// <c>TransactionService.UpdateTransaction</c> semantics: header update,
/// UDF replacement, customer + banking details, beneficiaries, stakeholders.
///
/// Workflow state is deliberately NOT touched here. Callers that also need
/// to advance the workflow call <c>PUT /Workflow/Process/{id}/Execute</c>
/// after this endpoint returns. That's the FE's save-then-execute pattern
/// in <c>dynamicForm.tsx</c>.
///
/// <para>
/// <b>Diff strategy on child collections</b> (beneficiaries, stakeholders,
/// customer banking details): rows whose id appears in the request are
/// UPDATEd; rows without an id are INSERTed; existing rows whose id is
/// NOT in the request are DELETEd. This matches the legacy "replace whole
/// child collection" semantic without forcing us to delete+re-insert
/// (which would burn IDENTITY values).
/// </para>
/// </summary>
public class UpdateTransactionCommandHandler
    : IRequestHandler<UpdateTransactionCommand, TransactionDetailDto>
{
    private readonly IApplicationDbContext   _db;
    private readonly ICurrentUserService     _current;
    private readonly IMediator               _mediator;
    private readonly IAutoFlagEvaluator      _autoFlags;
    private readonly ILogger<UpdateTransactionCommandHandler> _log;

    public UpdateTransactionCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current,
        IMediator             mediator,
        IAutoFlagEvaluator    autoFlags,
        ILogger<UpdateTransactionCommandHandler> log)
    {
        _db        = db;
        _current   = current;
        _mediator  = mediator;
        _autoFlags = autoFlags;
        _log       = log;
    }

    public async Task<TransactionDetailDto> Handle(UpdateTransactionCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Transaction update requires an authenticated caller.");

        var now = DateTime.UtcNow;

        // ---------- 1. Header ----------
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == req.transactionId, ct)
                 ?? throw new NotFoundException("transaction_not_found",
                        $"Transaction {req.transactionId} does not exist.");

        if (req.transactionDate.HasValue)      tx.TransactionDate     = req.transactionDate.Value;
        // Currency is now sourced from the UDF Currency field (a TmX_Lookup id) by
        // TransactionAmountResolver below, into Currency_Lkp — not from the header.
        if (req.transactionTypeLkp.HasValue)   tx.TransactionTypeLkp  = req.transactionTypeLkp.Value;
        if (req.transactionStatusLkp.HasValue) tx.TransactionStatusLkp = req.transactionStatusLkp.Value;
        if (req.clientReferenceNumber is not null) tx.ClientReferenceNumber = req.clientReferenceNumber;
        tx.LastUpdatedBy   = userId;
        tx.LastUpdatedDate = now;

        // ---------- 2. Transaction detail (UDF) ----------
        // The FE sends dynamic-form values as top-level objects keyed by each
        // field's Field_Table_Name (TmxTransactionDetail, TmxCustomerMaster,
        // TmxBeneficiaryDetail, …) — captured on the command via JsonExtensionData.
        // Prefer the explicit "TmxTransactionDetail" section; fall back to the
        // legacy flat `udfData` for older callers. When NEITHER is present we
        // preserve the existing UDF rather than nulling it (the prior bug).
        // Flat transaction fields (TmxTransactionDetail) live at the ROOT of the
        // detail blob; the long-tail grid/repeating sections (HS-code goods grid,
        // ports, etc.) are NESTED inside the SAME blob under their section key —
        // matching how existing ICBC_DEMO records store them. Both go into
        // TmX_Transaction_Detail.UDF_Data in one upsert.
        var detailSection  = UdfSectionMerge.TryGetSection(req.sections, "TmxTransactionDetail");
        var nestedSections = CollectNestedSections(req.sections);
        await UpsertTransactionDetailAsync(req.transactionId, detailSection, req.udfData, nestedSections, userId, now, ct);

        // NOTE: Sub_Product_Tab_Id is set once at create and is NOT changed
        // here — an update only mutates UDF values / minimal detail, never the
        // product or its sub-product.

        // ---------- 3. Customer snapshot + banking details ----------
        if (req.customer is not null)
            await UpsertCustomerAsync(req.transactionId, req.customer, userId, now, ct);

        // 3b. Customer-master field section → merge into the snapshot's UDF.
        // Merge (not replace) so server-managed keys like RiskScore survive.
        var customerSection = UdfSectionMerge.TryGetSection(req.sections, "TmxCustomerMaster");
        if (customerSection.HasValue)
            await MergeCustomerSectionUdfAsync(req.transactionId, customerSection.Value, userId, now, ct);

        // 3c. Promote first-class customer columns from the TmxCustomerMaster
        // field section into the snapshot's real columns (Customer_Name,
        // Customer_Code). The dynamic form stores the KYC "Customer Name"
        // (Section A) inside this section — legacy read it as
        // TmxCustomerMaster[0].CustomerName for the FCCM DisplayName. Without
        // this promotion the name only ever lands in the UDF blob, so the
        // Inbox / Trade Repository / onboarding DisplayName (all of which read
        // the Customer_Name COLUMN) stay blank for a brand-new KYC onboarding
        // customer that BRAINS/Customer-Master couldn't name at create time.
        // Handles both the object and legacy array ([{…}]) section shapes.
        await PromoteCustomerColumnsAsync(req.transactionId, req.sections, userId, now, ct);

        // ---------- 4. Beneficiaries ----------
        // The array-driven diff is full-replace, so an empty `beneficiaries: []`
        // (which the FE always sends — it drives beneficiary data through the
        // TmxBeneficiaryDetail section instead) would DELETE every row. Only run
        // the diff when the FE actually supplies array items; otherwise leave the
        // rows alone and let the section path own the beneficiary UDF.
        if (req.beneficiaries is { Count: > 0 })
            await DiffSyncUdfChildrenAsync(
                existing:    await _db.BeneficiaryDetails.Where(b => b.TransactionId == req.transactionId).ToListAsync(ct),
                incoming:    req.beneficiaries,
                idSelector:  b => b.BeneficiaryDetailId,
                create:      udf => new BeneficiaryDetail
                {
                    TenantId    = tx.TenantId,
                    TransactionId = req.transactionId,
                    UdfData     = udf,
                    CreatedBy   = userId,
                    CreatedDate = now
                },
                updateUdf:   (entity, udf) =>
                {
                    entity.UdfData         = udf;
                    entity.LastUpdatedBy   = userId;
                    entity.LastUpdatedDate = now;
                },
                remove:      e => _db.BeneficiaryDetails.Remove(e),
                add:         e => _db.BeneficiaryDetails.Add(e));

        // 4b. Beneficiary field section → merge into the primary beneficiary's UDF
        // (create one if none exists yet — single-beneficiary Trade flow).
        var beneficiarySection = UdfSectionMerge.TryGetSection(req.sections, "TmxBeneficiaryDetail");
        if (beneficiarySection.HasValue)
            await MergePrimaryBeneficiarySectionUdfAsync(req.transactionId, tx.TenantId, beneficiarySection.Value, userId, now, ct);

        // ---------- 5. Stakeholders ----------
        // Same empty-array guard as beneficiaries (avoid wiping rows on the
        // GET-shape `stakeholders: []` the FE round-trips).
        if (req.stakeholders is { Count: > 0 })
            await DiffSyncUdfChildrenAsync(
                existing:    await _db.TransactionStakeholders.Where(s => s.TransactionId == req.transactionId).ToListAsync(ct),
                incoming:    req.stakeholders,
                idSelector:  s => s.TransactionStakeholderId,
                create:      udf => new TransactionStakeholder
                {
                    TenantId      = tx.TenantId,
                    TransactionId = req.transactionId,
                    UdfData       = udf,
                    CreatedBy     = userId,
                    CreatedDate   = now
                },
                updateUdf:   (entity, udf) =>
                {
                    entity.UdfData         = udf;
                    entity.LastUpdatedBy   = userId;
                    entity.LastUpdatedDate = now;
                },
                remove:      e => _db.TransactionStakeholders.Remove(e),
                add:         e => _db.TransactionStakeholders.Add(e));

        // (Long-tail grid sections were already folded into the detail blob in
        // step 2 — see UpsertTransactionDetailAsync / CollectNestedSections.)

        // ---------- 5.5 Flags (Slice 8) ----------
        // Stage flag inserts / updates and append-only history rows.
        // The differ stages everything onto the same DbContext so the
        // SaveChangesAsync below commits transaction header + UDF +
        // children + flag state + history atomically.
        await TransactionFlagDiffer.ApplyAsync(
            _db, req.transactionId, req.flags ?? new(), userId, now, ct);

        // 5.6 Auto flags (rule engine) — evaluate the configured auto red-flag
        // rules in scope and stage Source=Auto results onto the same context,
        // committed by the SaveChanges below. Best-effort: a rule-engine fault
        // must not block the transaction save.
        try
        {
            await _autoFlags.EvaluateAndStageAsync(req.transactionId, userId, now, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auto-flag evaluation failed for transaction {Txn}; continuing save.", req.transactionId);
        }

        // Additively project the submitted UDF onto the typed Transaction_Amount /
        // Currency_Id columns (Is_Amount_Field field + Currency dropdown). The tx
        // entity is tracked, so these commit in the SaveChanges below. Never throws.
        await TransactionAmountResolver.ApplyAsync(_db, tx, req.udfData, ct);

        await _db.SaveChangesAsync(ct);

        // ---------- 6. checkbox_file → TransactionDocument bridge ----------
        // Surface attestation documents (checkbox_file fields) in the Documents
        // tab by linking their uploaded Document id into the TransactionDocument
        // bridge. Best-effort + add-only (the bridge also holds manual uploads,
        // so we never remove here) — a fault must not block the save.
        try
        {
            await LinkCheckboxFileDocumentsAsync(
                req.transactionId, tx.ProductId, tx.TenantId, req.sections, userId, now, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Linking checkbox_file documents failed for transaction {Txn}; continuing.",
                req.transactionId);
        }

        _log.LogInformation(
            "Transaction {TransactionId} updated by {UserId} — beneficiaries={BCount}, stakeholders={SCount}, flagChanges={FCount}.",
            req.transactionId, userId,
            req.beneficiaries?.Count ?? 0,
            req.stakeholders?.Count  ?? 0,
            req.flags?.Count         ?? 0);

        // 6. Return the canonical detail shape via the existing read handler.
        return await _mediator.Send(new GetTransactionByIdQuery(req.transactionId), ct);
    }

    // ============================================================
    // checkbox_file → TransactionDocument bridge
    // ============================================================

    /// <summary>
    /// Links the documents attached to <c>checkbox_file</c> attestation fields
    /// into the <c>TransactionDocument</c> bridge so they appear in the
    /// Documents tab. ADD-ONLY: the bridge also stores manually-uploaded
    /// attachments and carries no source marker, so we never remove here.
    /// checkbox_file is identified by the tenant's 'field' lookup display name
    /// ("Checkbox_file") — the same name-based detection the FE uses, since the
    /// type shares Field_Type_Lkp 2 with the date picker.
    /// </summary>
    private async Task LinkCheckboxFileDocumentsAsync(
        int transactionId, int productId, int tenantId,
        IReadOnlyDictionary<string, JsonElement> sections,
        string userId, DateTime now, CancellationToken ct)
    {
        if (sections is null || sections.Count == 0) return;

        // checkbox_file field-type codes (by lookup display name).
        var docTypeCodes = (await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "field"
                         && l.HiddenValue != null
                         && l.VisibleValue != null)
                .Select(l => new { l.HiddenValue, l.VisibleValue })
                .ToListAsync(ct))
            .Where(r => (r.VisibleValue ?? string.Empty)
                            .Replace("_", "").Replace("-", "").Trim()
                            .Equals("checkboxfile", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(r.HiddenValue, out _))
            .Select(r => int.Parse(r.HiddenValue!))
            .ToHashSet();
        if (docTypeCodes.Count == 0) return;

        // checkbox_file field NAMES configured for this product.
        var fieldNames = (await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.ProductId == productId
                         && f.IsActive
                         && f.FieldTypeLkp != null
                         && f.FieldName != null
                         && docTypeCodes.Contains(f.FieldTypeLkp.Value))
                .Select(f => f.FieldName!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fieldNames.Count == 0) return;

        // Collect submitted documentIds for those fields (scan every section bag;
        // field names are unique across sections).
        var docIds = new HashSet<int>();
        foreach (var section in sections.Values)
        {
            if (section.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in section.EnumerateObject())
            {
                if (!fieldNames.Contains(prop.Name)) continue;
                if (TryReadDocumentId(prop.Value, out var id)) docIds.Add(id);
            }
        }
        if (docIds.Count == 0) return;

        var alreadyLinked = (await _db.TransactionDocuments.AsNoTracking()
                .Where(td => td.TransactionId == transactionId && td.ActiveFlag)
                .Select(td => td.DocumentId)
                .ToListAsync(ct))
            .ToHashSet();

        var toLink = docIds.Where(id => !alreadyLinked.Contains(id)).ToList();
        if (toLink.Count == 0) return;

        // Only bridge ids backed by a real Document row (skip stale/legacy values).
        var validIds = (await _db.Documents.AsNoTracking()
                .Where(d => toLink.Contains(d.DocumentId))
                .Select(d => d.DocumentId)
                .ToListAsync(ct))
            .ToHashSet();

        var added = 0;
        foreach (var id in toLink)
        {
            if (!validIds.Contains(id)) continue;
            _db.TransactionDocuments.Add(new Domain.Entities.Documents.TransactionDocument
            {
                TransactionId = transactionId,
                DocumentId    = id,
                TenantId      = tenantId,
                ActiveFlag    = true,
                CreatedBy     = userId,
                CreatedDate   = now,
            });
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync(ct);
    }

    private static bool TryReadDocumentId(JsonElement value, out int id)
    {
        id = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out id) && id > 0,
            JsonValueKind.String => int.TryParse(value.GetString(), out id) && id > 0,
            _ => false,
        };
    }

    // ============================================================
    // UDF helpers
    // ============================================================

    /// <summary>
    /// Serializes the parsed UDF object to nvarchar(max) JSON. Null input
    /// produces null output (the column is nullable). A non-null but empty
    /// object serializes to "{}".
    /// </summary>
    private static string? SerializeUdf(object? udf) =>
        udf is null ? null : JsonSerializer.Serialize(udf);

    /// <summary>
    /// Known dynamic-form sections that map onto a first-class entity. Anything
    /// the FE sends that isn't one of these (echoed GET fields, unmodeled child
    /// tables) is ignored by the routing and — for table-shaped names — logged.
    /// </summary>
    private static readonly HashSet<string> MappedSectionNames =
        new(StringComparer.OrdinalIgnoreCase) { "TmxTransactionDetail", "TmxCustomerMaster", "TmxBeneficiaryDetail" };

    /// <summary>
    /// Collects the long-tail grid/repeating sections the FE sent — the
    /// <c>Tmx*</c> keys that aren't one of the entity-backed
    /// <see cref="MappedSectionNames"/> and aren't echoed GET fields — to be
    /// nested inside the transaction-detail blob. Object AND array values are
    /// kept (grids are arrays).
    /// </summary>
    private static List<KeyValuePair<string, JsonElement>> CollectNestedSections(Dictionary<string, JsonElement> sections)
    {
        var list = new List<KeyValuePair<string, JsonElement>>();
        if (sections is null) return list;
        foreach (var kv in sections)
        {
            if (!kv.Key.StartsWith("Tmx", StringComparison.Ordinal)) continue; // echoed GET field
            if (MappedSectionNames.Contains(kv.Key)) continue;                 // entity-backed → own table
            var k = kv.Value.ValueKind;
            if (k != JsonValueKind.Object && k != JsonValueKind.Array) continue;
            list.Add(kv);
        }
        return list;
    }

    private async Task UpsertTransactionDetailAsync(
        int transactionId, JsonElement? section, object? legacyUdf,
        IReadOnlyList<KeyValuePair<string, JsonElement>> nestedSections,
        string userId, DateTime now, CancellationToken ct)
    {
        var detail = await _db.TransactionDetails.FirstOrDefaultAsync(d => d.TransactionId == transactionId, ct);

        // Build the detail blob: flat TmxTransactionDetail fields at the root
        // (merged over existing — keys not resent survive), the long-tail grid
        // sections nested under their own key, and legacy flat `udfData` as a
        // back-compat fallback. When there's nothing to apply, the existing UDF
        // is preserved (NOT nulled).
        var json = UdfSectionMerge.MergeDetail(detail?.UdfData, section, legacyUdf, nestedSections);

        // ESI (Economic Substance Indicator) — derive + persist the authoritative outcome
        // when this payload carries ESI checkpoint answers (KYC form). Best-effort.
        json = await ApplyEsiOutcomeAsync(json, ct);

        if (detail is null)
        {
            // Brand-new detail row — INSERT.
            _db.TransactionDetails.Add(new TransactionDetail
            {
                TenantId      = 1,                         // single-tenant deployment; matches Slice 6 Step 3
                TransactionId = transactionId,
                UdfData       = json,
                CreatedBy     = userId,
                CreatedDate   = now
            });
        }
        else
        {
            // Existing — UPDATE in place.
            detail.UdfData         = json;
            detail.LastUpdatedBy   = userId;
            detail.LastUpdatedDate = now;
        }
    }

    // ---- ESI (Economic Substance Indicator) compute-at-submit -------------
    private sealed class EsiWrap { public EsiMeta? esi { get; set; } }
    private sealed class EsiMeta
    {
        public string?   code     { get; set; }
        public bool?     inCalc   { get; set; }
        public string[]? highRisk { get; set; }
    }

    /// <summary>
    /// When the detail UDF carries ESI checkpoint answers (KYC form), derive the composite
    /// outcome from each checkpoint's own metadata (<c>udfData.esi.highRisk</c> matched against
    /// the selected option's <c>Hidden_Value</c>) and write the authoritative result back into
    /// the UDF: <c>ESI_Outcome</c> ("High"/"Adequate"), <c>ESI_Failed_Codes</c> (comma list),
    /// <c>ESI_Assessed_At</c>, <c>ESI_Ruleset_Version</c>. Fully metadata-driven — no hard-coded
    /// checkpoint list. Best-effort: any failure leaves the UDF untouched (never blocks the save).
    /// </summary>
    private async Task<string> ApplyEsiOutcomeAsync(string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"ESI_", StringComparison.Ordinal))
            return json;   // no ESI answers in this payload.
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return json;

            var esiFields = await _db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.FieldLookupType == "ESI_YES_NO_NA" && f.FieldName != null && f.UdfData != null)
                .Select(f => new { f.FieldName, f.UdfData })
                .ToListAsync(ct);
            if (esiFields.Count == 0) return json;

            var hiddenById = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "ESI_YES_NO_NA")
                .Select(l => new { l.Id, l.HiddenValue })
                .ToDictionaryAsync(x => x.Id.ToString(), x => x.HiddenValue ?? "", ct);

            var failed = new List<string>();
            foreach (var f in esiFields)
            {
                EsiMeta? meta = null;
                try { meta = JsonSerializer.Deserialize<EsiWrap>(f.UdfData!)?.esi; } catch { /* skip malformed */ }
                if (meta is null || meta.inCalc != true || string.IsNullOrEmpty(meta.code)) continue;

                var answerId = root[f.FieldName!]?.ToString();
                if (string.IsNullOrEmpty(answerId)) continue;

                var hidden = hiddenById.TryGetValue(answerId!, out var hv) ? hv : answerId!;
                if (meta.highRisk is { Length: > 0 } hr &&
                    hr.Contains(hidden, StringComparer.OrdinalIgnoreCase))
                    failed.Add(meta.code!);
            }

            root["ESI_Failed_Codes"]    = string.Join(",", failed);
            root["ESI_Outcome"]         = failed.Count > 0 ? "High" : "Adequate";
            root["ESI_Assessed_At"]     = DateTime.UtcNow.ToString("o");
            root["ESI_Ruleset_Version"] = "esi-v1";

            _log.LogInformation("ESI outcome computed: {Outcome} (failed: {Failed}).",
                failed.Count > 0 ? "High" : "Adequate", string.Join(",", failed));
            return root.ToJsonString();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ESI outcome compute skipped (non-fatal).");
            return json;
        }
    }

    /// <summary>
    /// Field names inside the <c>TmxCustomerMaster</c> section that map onto a
    /// first-class column on <see cref="TransactionCustomerSnapshot"/> (rather
    /// than staying in the UDF blob). Matched case-insensitively.
    /// </summary>
    private static readonly string[] CustomerNameKeys = { "CustomerName", "Customer_Name" };
    private static readonly string[] CustomerCodeKeys = { "CustomerCode", "Customer_Code" };

    /// <summary>
    /// Promotes the customer name (and code) from the <c>TmxCustomerMaster</c>
    /// field section into the snapshot's real columns so every screen that reads
    /// <c>Customer_Name</c> (Inbox listing, Trade Repository, transaction header,
    /// FCCM onboarding <c>DisplayName</c>) shows it. The dynamic form keeps these
    /// in the section bag; legacy read them off the same section as
    /// <c>TmxCustomerMaster[0].CustomerName</c>. Non-destructive: only overwrites
    /// a column when the section carries a non-empty value. Tolerates both the
    /// object (<c>{…}</c>) and legacy array (<c>[{…}]</c>) section shapes.
    /// </summary>
    private async Task PromoteCustomerColumnsAsync(
        int transactionId, IReadOnlyDictionary<string, JsonElement>? sections,
        string userId, DateTime now, CancellationToken ct)
    {
        if (sections is null) return;

        // Locate the section case-insensitively; normalize array → first element.
        JsonElement? obj = null;
        foreach (var kv in sections)
        {
            if (!string.Equals(kv.Key, "TmxCustomerMaster", StringComparison.OrdinalIgnoreCase))
                continue;
            if (kv.Value.ValueKind == JsonValueKind.Object)
                obj = kv.Value;
            else if (kv.Value.ValueKind == JsonValueKind.Array
                     && kv.Value.GetArrayLength() > 0
                     && kv.Value[0].ValueKind == JsonValueKind.Object)
                obj = kv.Value[0];
            break;
        }
        if (obj is null) return;

        var name = ReadStringKey(obj.Value, CustomerNameKeys);
        var code = ReadStringKey(obj.Value, CustomerCodeKeys);
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code)) return;

        var snapshot = await _db.TransactionCustomerSnapshots
            .FirstOrDefaultAsync(c => c.TransactionId == transactionId, ct);
        if (snapshot is null) return; // create always inserts one; nothing to promote onto otherwise

        var changed = false;
        if (!string.IsNullOrWhiteSpace(name) && snapshot.CustomerName != name)
        {
            snapshot.CustomerName = name;
            changed = true;
        }
        // Only fill the code when it's currently blank — it's the applicant id set
        // at create and shouldn't be silently rewritten by a form edit.
        if (!string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(snapshot.CustomerCode))
        {
            snapshot.CustomerCode = code;
            changed = true;
        }
        if (changed)
        {
            snapshot.LastUpdatedBy   = userId;
            snapshot.LastUpdatedDate = now;
        }
    }

    /// <summary>Reads the first present, non-empty string value among the given
    /// candidate keys from a JSON object (case-insensitive).</summary>
    private static string? ReadStringKey(JsonElement obj, string[] keys)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in obj.EnumerateObject())
            foreach (var key in keys)
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    var v = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
        return null;
    }

    /// <summary>
    /// Merges the <c>TmxCustomerMaster</c> field section into the transaction's
    /// customer snapshot UDF (the single row per transaction). Inserts a snapshot
    /// if none exists yet.
    /// </summary>
    private async Task MergeCustomerSectionUdfAsync(int transactionId, JsonElement section, string userId, DateTime now, CancellationToken ct)
    {
        var snapshot = await _db.TransactionCustomerSnapshots
            .FirstOrDefaultAsync(c => c.TransactionId == transactionId, ct);

        if (snapshot is null)
        {
            _db.TransactionCustomerSnapshots.Add(new TransactionCustomerSnapshot
            {
                TenantId      = 1,
                TransactionId = transactionId,
                UdfData       = UdfSectionMerge.Merge(null, section),
                CreatedBy     = userId,
                CreatedDate   = now
            });
        }
        else
        {
            snapshot.UdfData         = UdfSectionMerge.Merge(snapshot.UdfData, section);
            snapshot.LastUpdatedBy   = userId;
            snapshot.LastUpdatedDate = now;
        }
    }

    /// <summary>
    /// Merges the <c>TmxBeneficiaryDetail</c> field section into the primary
    /// beneficiary row's UDF (lowest BeneficiaryDetailId). Single-beneficiary
    /// Trade flow: if no beneficiary row exists, one is created to hold the UDF.
    /// </summary>
    private async Task MergePrimaryBeneficiarySectionUdfAsync(int transactionId, int tenantId, JsonElement section, string userId, DateTime now, CancellationToken ct)
    {
        var ben = await _db.BeneficiaryDetails
            .Where(b => b.TransactionId == transactionId)
            .OrderBy(b => b.BeneficiaryDetailId)
            .FirstOrDefaultAsync(ct);

        if (ben is null)
        {
            _db.BeneficiaryDetails.Add(new BeneficiaryDetail
            {
                TenantId      = tenantId,
                TransactionId = transactionId,
                UdfData       = UdfSectionMerge.Merge(null, section),
                CreatedBy     = userId,
                CreatedDate   = now
            });
        }
        else
        {
            ben.UdfData         = UdfSectionMerge.Merge(ben.UdfData, section);
            ben.LastUpdatedBy   = userId;
            ben.LastUpdatedDate = now;
        }
    }

    private async Task UpsertCustomerAsync(int transactionId, UpdateCustomerInput cust, string userId, DateTime now, CancellationToken ct)
    {
        TransactionCustomerSnapshot? snapshot = null;

        if (cust.customerMasterId is > 0)
        {
            snapshot = await _db.TransactionCustomerSnapshots
                .FirstOrDefaultAsync(c => c.CustomerMasterId == cust.customerMasterId.Value && c.TransactionId == transactionId, ct);
        }
        snapshot ??= await _db.TransactionCustomerSnapshots
            .FirstOrDefaultAsync(c => c.TransactionId == transactionId, ct);

        var udfJson = SerializeUdf(cust.udfData);

        if (snapshot is null)
        {
            // No existing snapshot — INSERT new.
            snapshot = new TransactionCustomerSnapshot
            {
                TenantId                = 1,
                TransactionId           = transactionId,
                CustomerCode            = cust.customerCode,
                CustomerName            = cust.customerName,
                CustomerTitle           = cust.customerTitle,
                NationalIdentifierValue = cust.nationalIdentifierValue,
                NationalIdTypeLkp       = cust.nationalIdTypeLkp,
                CustomerTypeLkp         = cust.customerTypeLkp,
                CustomerSegmentLkp      = cust.customerSegmentLkp,
                CustomerSubSegmentLkp   = cust.customerSubSegmentLkp,
                LocationId              = cust.locationId,
                UdfData                 = udfJson,
                CreatedBy               = userId,
                CreatedDate             = now
            };
            _db.TransactionCustomerSnapshots.Add(snapshot);
            // Save so we have CustomerMasterId for the banking details.
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (cust.customerCode is not null)            snapshot.CustomerCode            = cust.customerCode;
            if (cust.customerName is not null)            snapshot.CustomerName            = cust.customerName;
            if (cust.customerTitle is not null)           snapshot.CustomerTitle           = cust.customerTitle;
            if (cust.nationalIdentifierValue is not null) snapshot.NationalIdentifierValue = cust.nationalIdentifierValue;
            if (cust.nationalIdTypeLkp.HasValue)          snapshot.NationalIdTypeLkp       = cust.nationalIdTypeLkp;
            if (cust.customerTypeLkp.HasValue)            snapshot.CustomerTypeLkp         = cust.customerTypeLkp;
            if (cust.customerSegmentLkp.HasValue)         snapshot.CustomerSegmentLkp      = cust.customerSegmentLkp;
            if (cust.customerSubSegmentLkp.HasValue)      snapshot.CustomerSubSegmentLkp   = cust.customerSubSegmentLkp;
            if (cust.locationId.HasValue)                 snapshot.LocationId              = cust.locationId;
            if (cust.udfData is not null)                 snapshot.UdfData                 = udfJson;
            snapshot.LastUpdatedBy   = userId;
            snapshot.LastUpdatedDate = now;
        }

        // Banking details — diff strategy nested under the customer snapshot.
        var existingBanks = await _db.CustomerBankingDetails
            .Where(b => b.CustomerMasterId == snapshot.CustomerMasterId)
            .ToListAsync(ct);

        DiffSync(
            existing:   existingBanks,
            incoming:   cust.bankingDetails,
            idSelector: b => b.CustomerBankingDetailId,
            keyOf:      i => i.customerBankingDetailId ?? 0,
            create:     input => new CustomerBankingDetail
            {
                TenantId            = 1,
                CustomerMasterId    = snapshot.CustomerMasterId,
                BankAccountNumber   = input.bankAccountNumber,
                BranchCode          = input.branchCode,
                BankCardNumber      = input.bankCardNumber,
                CardMemberName      = input.cardMemberName,
                AddressTypeLkpId    = input.addressTypeLkpId,
                InternetBanking     = input.internetBanking,
                InternetBankingTransactionAmount = input.internetBankingTransactionAmount,
                InternetAtmTransactionAmount     = input.internetAtmTransactionAmount,
                MailingCommunication = input.mailingCommunication,
                ChequeBookNumber    = input.chequeBookNumber,
                UdfData             = SerializeUdf(input.udfData),
                ActiveFlag          = true,
                EffectiveStartDate  = now,
                EffectiveEndDate    = now.AddYears(50),
                CreatedBy           = userId,
                CreatedDate         = now
            },
            updateExisting: (entity, input) =>
            {
                if (input.bankAccountNumber is not null) entity.BankAccountNumber = input.bankAccountNumber;
                if (input.branchCode is not null)        entity.BranchCode        = input.branchCode;
                if (input.bankCardNumber is not null)    entity.BankCardNumber    = input.bankCardNumber;
                if (input.cardMemberName is not null)    entity.CardMemberName    = input.cardMemberName;
                if (input.addressTypeLkpId.HasValue)     entity.AddressTypeLkpId  = input.addressTypeLkpId;
                if (input.internetBanking.HasValue)      entity.InternetBanking   = input.internetBanking;
                if (input.internetBankingTransactionAmount.HasValue) entity.InternetBankingTransactionAmount = input.internetBankingTransactionAmount;
                if (input.internetAtmTransactionAmount.HasValue)     entity.InternetAtmTransactionAmount     = input.internetAtmTransactionAmount;
                if (input.mailingCommunication is not null) entity.MailingCommunication = input.mailingCommunication;
                if (input.chequeBookNumber    is not null) entity.ChequeBookNumber    = input.chequeBookNumber;
                if (input.udfData is not null)             entity.UdfData             = SerializeUdf(input.udfData);
                entity.LastUpdatedBy   = userId;
                entity.LastUpdatedDate = now;
            },
            remove: e => _db.CustomerBankingDetails.Remove(e),
            add:    e => _db.CustomerBankingDetails.Add(e));
    }

    // ============================================================
    // Generic diff helpers
    // ============================================================

    /// <summary>
    /// Diff-syncs a collection of UDF-only children (beneficiaries,
    /// stakeholders) against the request. Rows with an <c>id</c> are
    /// UPDATEd; rows without an id are INSERTed; existing rows whose id
    /// is NOT in the request are DELETEd.
    /// </summary>
    private Task DiffSyncUdfChildrenAsync<TEntity>(
        List<TEntity>            existing,
        List<UdfChildInput>      incoming,
        Func<TEntity, int>       idSelector,
        Func<string?, TEntity>   create,
        Action<TEntity, string?> updateUdf,
        Action<TEntity>          remove,
        Action<TEntity>          add)
    {
        var incomingIds = incoming.Where(i => i.id is > 0).Select(i => i.id!.Value).ToHashSet();

        // DELETE: existing rows whose id isn't in incoming.
        foreach (var e in existing.Where(e => !incomingIds.Contains(idSelector(e))))
            remove(e);

        foreach (var input in incoming)
        {
            var udf = SerializeUdf(input.udfData);
            if (input.id is > 0)
            {
                // UPDATE: match the existing row, mutate udf.
                var match = existing.FirstOrDefault(e => idSelector(e) == input.id!.Value);
                if (match is not null) updateUdf(match, udf);
            }
            else
            {
                // INSERT.
                add(create(udf));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// More general diff helper — used for banking details where the
    /// input has many fields, not just udfData.
    /// </summary>
    private static void DiffSync<TEntity, TInput>(
        List<TEntity>             existing,
        List<TInput>              incoming,
        Func<TEntity, int>        idSelector,
        Func<TInput, int>         keyOf,
        Func<TInput, TEntity>     create,
        Action<TEntity, TInput>   updateExisting,
        Action<TEntity>           remove,
        Action<TEntity>           add)
    {
        var incomingIds = incoming.Select(keyOf).Where(k => k > 0).ToHashSet();

        foreach (var e in existing.Where(e => !incomingIds.Contains(idSelector(e))))
            remove(e);

        foreach (var input in incoming)
        {
            var key = keyOf(input);
            if (key > 0)
            {
                var match = existing.FirstOrDefault(e => idSelector(e) == key);
                if (match is not null) updateExisting(match, input);
            }
            else
            {
                add(create(input));
            }
        }
    }
}
