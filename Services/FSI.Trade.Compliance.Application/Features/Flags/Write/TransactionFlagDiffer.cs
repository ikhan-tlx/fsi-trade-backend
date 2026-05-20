using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Transactions.Update;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Write;

/// <summary>
/// Applies a list of incoming <see cref="UpdateFlagInput"/> rows to the
/// flag state for a single transaction, emitting append-only
/// <see cref="TransactionFlagHistory"/> entries for every actual state
/// transition.
///
/// Algorithm:
///   1. Load existing <c>TmX_Transaction_Flag</c> rows for the
///      transaction, keyed by Flag_ID.
///   2. For each input row:
///        • If no existing row and Is_Flagged = false and no notes /
///          evidence → skip (no-op).
///        • If no existing row otherwise → INSERT a new
///          TransactionFlag, emit "SET" history (or
///          Notes_Updated / Evidence_Attached if only those changed).
///        • If existing row exists → UPDATE in place; emit one history
///          row per distinct change dimension (state, notes, evidence).
///   3. Inputs are matched by Flag_ID. Flags NOT in the input list are
///      LEFT ALONE — this is the FE preference (no "clear everything"
///      semantics; analyst clears explicitly by sending isFlagged=false).
///
/// The differ never deletes a TransactionFlag row — soft state changes
/// via Is_Flagged=false preserve the audit trail.
/// </summary>
public static class TransactionFlagDiffer
{
    public static async Task ApplyAsync(
        IApplicationDbContext db,
        int transactionId,
        IReadOnlyList<UpdateFlagInput> inputs,
        string userId,
        DateTime now,
        CancellationToken ct)
    {
        if (inputs is null || inputs.Count == 0) return;

        // Single pass to resolve all change-type lookup IDs we might need.
        // FLAG_CHANGE_TYPE has 5 rows total — cheap to load whole.
        var changeTypeIds = await db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "FLAG_CHANGE_TYPE")
            .ToDictionaryAsync(l => l.HiddenValue!, l => l.Id, ct);

        if (!changeTypeIds.TryGetValue("SET",                 out var changeSet)        ||
            !changeTypeIds.TryGetValue("CLEARED",             out var changeCleared)    ||
            !changeTypeIds.TryGetValue("NOTES_UPDATED",       out var changeNotes)      ||
            !changeTypeIds.TryGetValue("EVIDENCE_ATTACHED",   out var changeEvAdded)    ||
            !changeTypeIds.TryGetValue("EVIDENCE_REMOVED",    out var changeEvRemoved))
        {
            throw new InvalidOperationException(
                "FLAG_CHANGE_TYPE lookup rows missing. Run 2026_05_012_SeedFlagLookups.sql.");
        }

        // Validate every incoming flag actually exists in the catalogue.
        // Cheap up-front check beats failing inside the loop.
        var inputFlagIds = inputs.Select(i => i.flagId).Distinct().ToList();
        var knownFlagIds = await db.FlagCatalogues.AsNoTracking()
            .Where(c => inputFlagIds.Contains(c.FlagId))
            .Select(c => c.FlagId)
            .ToListAsync(ct);
        var unknown = inputFlagIds.Except(knownFlagIds).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"UpdateTransactionCommand.flags references unknown Flag_ID(s): {string.Join(",", unknown)}");

        // Load existing transaction-flag rows for the diff.
        var existing = await db.TransactionFlags
            .Where(t => t.TransactionId == transactionId && inputFlagIds.Contains(t.FlagId))
            .ToDictionaryAsync(t => t.FlagId, ct);

        foreach (var input in inputs)
        {
            if (existing.TryGetValue(input.flagId, out var current))
            {
                ApplyToExisting(db, current, input, userId, now,
                    changeSet, changeCleared, changeNotes, changeEvAdded, changeEvRemoved);
            }
            else
            {
                ApplyAsNew(db, transactionId, input, userId, now,
                    changeSet, changeNotes, changeEvAdded);
            }
        }
    }

    private static void ApplyToExisting(
        IApplicationDbContext db,
        TransactionFlag       current,
        UpdateFlagInput       input,
        string                userId,
        DateTime              now,
        int changeSet, int changeCleared, int changeNotes, int changeEvAdded, int changeEvRemoved)
    {
        var stateChanged    = current.IsFlagged          != input.isFlagged;
        var notesChanged    = !string.Equals(current.AnalystNotes, input.analystNotes, StringComparison.Ordinal);
        var evidenceChanged = current.EvidenceDocumentId != input.evidenceDocumentId;

        if (!stateChanged && !notesChanged && !evidenceChanged) return;

        // History entries — one per dimension that changed. Captures the
        // before / after at the moment of change so audit reads are
        // self-contained.
        if (stateChanged)
        {
            db.TransactionFlagHistories.Add(new TransactionFlagHistory
            {
                TransactionFlagId          = current.TransactionFlagId,
                TransactionId              = current.TransactionId,
                FlagId                     = current.FlagId,
                ChangeTypeLkpId            = input.isFlagged ? changeSet : changeCleared,
                PreviousIsFlagged          = current.IsFlagged,
                NewIsFlagged               = input.isFlagged,
                PreviousNotes              = null,
                NewNotes                   = null,
                PreviousEvidenceDocumentId = null,
                NewEvidenceDocumentId      = null,
                ChangedBy                  = userId,
                ChangedDate                = now,
            });
        }

        if (notesChanged)
        {
            db.TransactionFlagHistories.Add(new TransactionFlagHistory
            {
                TransactionFlagId          = current.TransactionFlagId,
                TransactionId              = current.TransactionId,
                FlagId                     = current.FlagId,
                ChangeTypeLkpId            = changeNotes,
                PreviousIsFlagged          = null,
                NewIsFlagged               = null,
                PreviousNotes              = current.AnalystNotes,
                NewNotes                   = input.analystNotes,
                PreviousEvidenceDocumentId = null,
                NewEvidenceDocumentId      = null,
                ChangedBy                  = userId,
                ChangedDate                = now,
            });
        }

        if (evidenceChanged)
        {
            var changeType = (input.evidenceDocumentId, current.EvidenceDocumentId) switch
            {
                (not null, null) => changeEvAdded,
                (null, not null) => changeEvRemoved,
                _                => changeEvAdded   // replaced one with another — treat as re-attach
            };

            db.TransactionFlagHistories.Add(new TransactionFlagHistory
            {
                TransactionFlagId          = current.TransactionFlagId,
                TransactionId              = current.TransactionId,
                FlagId                     = current.FlagId,
                ChangeTypeLkpId            = changeType,
                PreviousIsFlagged          = null,
                NewIsFlagged               = null,
                PreviousNotes              = null,
                NewNotes                   = null,
                PreviousEvidenceDocumentId = current.EvidenceDocumentId,
                NewEvidenceDocumentId      = input.evidenceDocumentId,
                ChangedBy                  = userId,
                ChangedDate                = now,
            });
        }

        // Apply the changes to the current row.
        current.IsFlagged          = input.isFlagged;
        current.AnalystNotes       = input.analystNotes;
        current.EvidenceDocumentId = input.evidenceDocumentId;
        current.SetBy              = userId;
        current.SetDate            = now;
        current.LastUpdatedBy      = userId;
        current.LastUpdatedDate    = now;
    }

    private static void ApplyAsNew(
        IApplicationDbContext db,
        int                   transactionId,
        UpdateFlagInput       input,
        string                userId,
        DateTime              now,
        int changeSet, int changeNotes, int changeEvAdded)
    {
        // Skip no-op inserts: an input with isFlagged=false AND no notes
        // AND no evidence is the analyst confirming "nothing to record"
        // and shouldn't bloat the table.
        if (!input.isFlagged
            && string.IsNullOrWhiteSpace(input.analystNotes)
            && input.evidenceDocumentId is null)
        {
            return;
        }

        var tf = new TransactionFlag
        {
            TransactionId       = transactionId,
            FlagId              = input.flagId,
            IsFlagged           = input.isFlagged,
            EvidenceDocumentId  = input.evidenceDocumentId,
            AnalystNotes        = input.analystNotes,
            SetBy               = userId,
            SetDate             = now,
            CreatedBy           = userId,
            CreatedDate         = now,
        };
        db.TransactionFlags.Add(tf);

        // History: SET (or just Notes/Evidence change if isFlagged is
        // false but the other dimensions carry data — rare but possible).
        // Note: the FK on TransactionFlagHistory.TransactionFlagId will
        // be resolved by EF's IDENTITY chaining when SaveChangesAsync
        // runs — we just leave it at default(int) here.
        if (input.isFlagged)
        {
            db.TransactionFlagHistories.Add(new TransactionFlagHistory
            {
                TransactionFlagId          = tf.TransactionFlagId,
                TransactionId              = transactionId,
                FlagId                     = input.flagId,
                ChangeTypeLkpId            = changeSet,
                PreviousIsFlagged          = null,
                NewIsFlagged               = true,
                PreviousNotes              = null,
                NewNotes                   = input.analystNotes,
                PreviousEvidenceDocumentId = null,
                NewEvidenceDocumentId      = input.evidenceDocumentId,
                ChangedBy                  = userId,
                ChangedDate                = now,
            });
        }
        else if (!string.IsNullOrWhiteSpace(input.analystNotes))
        {
            db.TransactionFlagHistories.Add(new TransactionFlagHistory
            {
                TransactionFlagId          = tf.TransactionFlagId,
                TransactionId              = transactionId,
                FlagId                     = input.flagId,
                ChangeTypeLkpId            = changeNotes,
                PreviousNotes              = null,
                NewNotes                   = input.analystNotes,
                ChangedBy                  = userId,
                ChangedDate                = now,
            });
        }
        else if (input.evidenceDocumentId is not null)
        {
            db.TransactionFlagHistories.Add(new TransactionFlagHistory
            {
                TransactionFlagId          = tf.TransactionFlagId,
                TransactionId              = transactionId,
                FlagId                     = input.flagId,
                ChangeTypeLkpId            = changeEvAdded,
                PreviousEvidenceDocumentId = null,
                NewEvidenceDocumentId      = input.evidenceDocumentId,
                ChangedBy                  = userId,
                ChangedDate                = now,
            });
        }
    }
}
