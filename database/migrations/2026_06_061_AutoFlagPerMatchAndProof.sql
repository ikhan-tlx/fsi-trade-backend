/* ============================================================================
   2026_06_061_AutoFlagPerMatchAndProof.sql

   Auto-flag "raise per match" toggle + developer match-proof.

   Adds, all additive and idempotent:

   1. TmX_Flag_Catalogue.Raise_Per_Match  (bit, NOT NULL, DEFAULT 0)
        Per-flag behaviour switch for the auto-flag engine:
          0 (default) = AGGREGATE  — one TmX_Transaction_Flag row per (txn,flag);
                        Matched_Value summarises every match. One FCCM event.
          1           = PER-MATCH  — one row per matching (field × value), so each
                        sanctioned country / screened good raises its own flag and
                        its own FCCM event (legacy "flag every field" parity).
        Default 0 preserves the behaviour of every existing seeded flag.

   2. TmX_Transaction_Flag.Match_Proof    (nvarchar(max), NULL)
        Developer-/QA-only diagnostic. For Source='Auto' rows the engine writes a
        JSON trace of WHY the flag did/didn't raise (per-condition resolved
        candidate values, operator, list, matched value, group verdict). NULL on
        manual rows. Never shown in the analyst UI; surfaced via the dry-run/Test
        panel and available for post-hoc verification.

   3. Uniqueness on TmX_Transaction_Flag widened from (Transaction_ID, Flag_ID) to
      (Transaction_ID, Flag_ID, Matched_Value) so PER-MATCH mode can store several
      rows for the same flag. Matched_Value is nvarchar(400) (added in
      2026_06_040), so it is index-key legal. Manual/aggregate rows keep a single
      row (NULLs compare equal in a UNIQUE index → one NULL per txn/flag).

   Safe to re-run. No data is modified; no existing flag changes behaviour.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* 1. Raise_Per_Match on TmX_Flag_Catalogue ----------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_Flag_Catalogue')
                 AND name = 'Raise_Per_Match')
BEGIN
    ALTER TABLE dbo.TmX_Flag_Catalogue
        ADD [Raise_Per_Match] bit NOT NULL
            CONSTRAINT [DF_FlagCatalogue_RaisePerMatch] DEFAULT (0);
    PRINT 'Added dbo.TmX_Flag_Catalogue.Raise_Per_Match (default 0 = aggregate).';
END
ELSE
    PRINT 'dbo.TmX_Flag_Catalogue.Raise_Per_Match already exists — skipped.';
GO

/* 2. Match_Proof on TmX_Transaction_Flag ------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_Transaction_Flag')
                 AND name = 'Match_Proof')
BEGIN
    ALTER TABLE dbo.TmX_Transaction_Flag
        ADD [Match_Proof] nvarchar(max) NULL;
    PRINT 'Added dbo.TmX_Transaction_Flag.Match_Proof (Auto-only dev diagnostic).';
END
ELSE
    PRINT 'dbo.TmX_Transaction_Flag.Match_Proof already exists — skipped.';
GO

/* 3. Widen uniqueness to include Matched_Value ------------------------------- */
-- Drop the old (Transaction_ID, Flag_ID) UNIQUE constraint if present.
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UQ_TmX_Transaction_Flag_TxnFlag'
             AND parent_object_id = OBJECT_ID('dbo.TmX_Transaction_Flag'))
BEGIN
    ALTER TABLE dbo.TmX_Transaction_Flag
        DROP CONSTRAINT [UQ_TmX_Transaction_Flag_TxnFlag];
    PRINT 'Dropped UNIQUE constraint UQ_TmX_Transaction_Flag_TxnFlag.';
END
ELSE
    PRINT 'UQ_TmX_Transaction_Flag_TxnFlag not present — skipped drop.';
GO

-- Create the widened UNIQUE index. Idempotent.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_TmX_Transaction_Flag_TxnFlagValue'
                 AND object_id = OBJECT_ID('dbo.TmX_Transaction_Flag'))
BEGIN
    CREATE UNIQUE INDEX [UQ_TmX_Transaction_Flag_TxnFlagValue]
        ON dbo.TmX_Transaction_Flag ([Transaction_ID], [Flag_ID], [Matched_Value]);
    PRINT 'Created UNIQUE index UQ_TmX_Transaction_Flag_TxnFlagValue (Txn, Flag, Matched_Value).';
END
ELSE
    PRINT 'UQ_TmX_Transaction_Flag_TxnFlagValue already exists — skipped.';
GO

PRINT '2026_06_061_AutoFlagPerMatchAndProof.sql complete.';
GO
