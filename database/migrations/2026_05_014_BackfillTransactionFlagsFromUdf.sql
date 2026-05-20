-- =========================================================================
-- 2026_05_014_BackfillTransactionFlagsFromUdf.sql
--
-- Slice 8 Step 4 — backfill TmX_Transaction_Flag (and history) from
-- existing UDF_Data JSON blobs on TmX_Transaction_Detail.
--
-- Approach
--   For each scope row in TmX_Flag_Scope (the 644 entries seeded by
--   Step 3, each with a Legacy_Field_Name like "ILFMRL5"), inspect every
--   TmX_Transaction_Detail.UDF_Data and emit a TmX_Transaction_Flag row
--   IF the JSON key for that legacy name resolves to "1" / 1 / true.
--
--   SQL Server's JSON_VALUE returns NULL for missing keys, so we filter
--   on the truthy values explicitly rather than relying on missing-key
--   semantics.
--
-- Orphan keys
--   Real production UDF_Data carries keys (e.g. "IifMRL3":1) that don't
--   exist in TmX_Tenant_Field_Setup — likely artifacts of an older form
--   schema that nobody migrated. The INNER JOIN against TmX_Flag_Scope
--   naturally drops them (no matching Legacy_Field_Name), which is the
--   intended behaviour per FSI direction (skip silently).
--   Step D prints a count of orphan ticks for visibility — useful audit
--   info without changing migration behaviour.
--
-- Collation
--   JSON_VALUE returns its result with Latin1_General_BIN2 collation
--   (SQL Server's case-sensitive default for the JSON parser). Comparing
--   that against literals or against other column collations can throw
--   "Cannot resolve the collation conflict between..." (Msg 468). The
--   queries below add explicit COLLATE DATABASE_DEFAULT to coerce the
--   JSON_VALUE result back to the DB's default collation before any
--   string comparison.
--
-- History
--   One TmX_Transaction_Flag_History row per inserted flag, with:
--     Change_Type_Lkp_ID = FLAG_CHANGE_TYPE 'SET'
--     Changed_By         = 'Backfill (Slice 8 migration)'
--     Changed_Date       = TmX_Transaction.Created_Date
--                          (best-effort — we can't reconstruct real
--                           toggle timeline, this honestly attributes
--                           the value to its origin transaction)
--     Previous_Is_Flagged = NULL  (no prior state on record)
--     New_Is_Flagged      = 1
--
-- Idempotency
--   Both inserts guarded by NOT EXISTS so partial runs can be resumed.
--   Re-running a complete backfill is a no-op.
--
-- Performance
--   Single set-based INSERT per table; JSON_VALUE is fully indexed-
--   friendly in SQL Server 2017+ on computed-column projections, but
--   for a one-shot migration we accept a sequential scan over
--   TmX_Transaction_Detail. On the demo data (a few thousand rows)
--   this completes in seconds. For production-scale (millions), the
--   migration should be batched — see "Batching note" below.
--
-- Batching note
--   For production deployments, replace the single INSERT with a
--   batched loop:  WHILE EXISTS rows ... INSERT TOP (5000) ... no
--   schema change needed. The current single-pass version is correct
--   on any size, just slower as a single transaction.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @who             nvarchar(100) = N'Backfill (Slice 8 migration)';
DECLARE @lkpChangeSet    int = (
    SELECT TOP 1 Lookup_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = 'FLAG_CHANGE_TYPE' AND Hidden_Value = 'SET'
);

IF @lkpChangeSet IS NULL
BEGIN
    RAISERROR ('FLAG_CHANGE_TYPE=SET lookup row not found. Run 2026_05_012_SeedFlagLookups.sql first.', 16, 1);
    RETURN;
END

PRINT CONCAT('Starting backfill. FLAG_CHANGE_TYPE.SET = ', @lkpChangeSet);

-- -------------------------------------------------------------------------
-- Step A — Materialise (Transaction_Id, Flag_ID, Set_Date) tuples for
-- every (transaction, scope) pair where the JSON key is truthy.
--
-- "$.<Legacy_Field_Name>" — JSON path against UDF_Data. The values were
-- written by the FE form save flow and the legacy backend, both of which
-- emit strings ("0"/"1") rather than booleans, so we match on '1', 'true',
-- and integer 1 for safety.
-- -------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#BackfillSet') IS NOT NULL DROP TABLE #BackfillSet;

SELECT
    td.Transaction_Id  AS Transaction_ID,
    s.Flag_ID          AS Flag_ID,
    -- Take whichever timestamp is available. Created_Date on the
    -- transaction is the most-honest anchor for "when this flag was
    -- first known to be set" given we can't reconstruct the real timeline.
    ISNULL(t.Created_Date, GETUTCDATE()) AS Set_Date,
    ISNULL(t.Created_By,   @who)         AS Set_By
INTO #BackfillSet
FROM   dbo.TmX_Flag_Scope s
INNER JOIN dbo.TmX_Transaction_Detail td
        ON td.UDF_Data IS NOT NULL
       AND td.UDF_Data <> ''
INNER JOIN dbo.TmX_Transaction t
        ON t.Transaction_Id = td.Transaction_Id
WHERE  s.Legacy_Field_Name IS NOT NULL
  AND  s.Legacy_Field_Name <> ''
  AND  LOWER(ISNULL(
            JSON_VALUE(td.UDF_Data, CONCAT('$."', s.Legacy_Field_Name, '"')),
            ''
       )) COLLATE DATABASE_DEFAULT IN ('1', 'true');

DECLARE @rawTuples int = (SELECT COUNT(*) FROM #BackfillSet);
PRINT CONCAT('Step A: ', @rawTuples, ' (Transaction, Flag) tuples found in UDF_Data.');

-- Some transactions may carry the same flag via multiple Legacy_Field_Names
-- (e.g. KYC product has the indicator just once, but Import LC has the same
-- text rendered via ILFMRL5 + ICRMRL5 + SGMRL5... only one ever wins per
-- transaction, but defensive dedup makes the migration immune to historical
-- data weirdness).
IF OBJECT_ID('tempdb..#BackfillSet_Dedup') IS NOT NULL DROP TABLE #BackfillSet_Dedup;

SELECT
    Transaction_ID,
    Flag_ID,
    MIN(Set_Date) AS Set_Date,
    MIN(Set_By)   AS Set_By
INTO #BackfillSet_Dedup
FROM   #BackfillSet
GROUP BY Transaction_ID, Flag_ID;

DECLARE @dedupTuples int = (SELECT COUNT(*) FROM #BackfillSet_Dedup);
PRINT CONCAT('Step A (dedup): ', @dedupTuples, ' unique (Transaction, Flag) tuples.');

-- -------------------------------------------------------------------------
-- Step B — Insert into TmX_Transaction_Flag.
-- Skip rows already present (idempotent re-run support).
-- -------------------------------------------------------------------------
INSERT INTO dbo.TmX_Transaction_Flag
    (Transaction_ID, Flag_ID, Is_Flagged, Evidence_Document_ID, Analyst_Notes,
     Set_By,         Set_Date,
     Created_By,     Created_Date)
SELECT
    bs.Transaction_ID,
    bs.Flag_ID,
    1                AS Is_Flagged,
    NULL             AS Evidence_Document_ID,
    NULL             AS Analyst_Notes,
    bs.Set_By,
    bs.Set_Date,
    @who             AS Created_By,
    bs.Set_Date      AS Created_Date
FROM   #BackfillSet_Dedup bs
WHERE  NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Transaction_Flag tf
    WHERE  tf.Transaction_ID = bs.Transaction_ID
      AND  tf.Flag_ID        = bs.Flag_ID
);

DECLARE @txnFlagInserted int = @@ROWCOUNT;
PRINT CONCAT('Step B: ', @txnFlagInserted, ' TmX_Transaction_Flag row(s) inserted.');

-- -------------------------------------------------------------------------
-- Step C — Insert one history row per inserted TmX_Transaction_Flag.
-- "Set" change type with NULL previous-state, honest Backfill attribution.
-- -------------------------------------------------------------------------
INSERT INTO dbo.TmX_Transaction_Flag_History
    (Transaction_Flag_ID, Transaction_ID, Flag_ID,
     Change_Type_Lkp_ID,  Previous_Is_Flagged, New_Is_Flagged,
     Previous_Notes,      New_Notes,
     Previous_Evidence_Document_ID, New_Evidence_Document_ID,
     Changed_By,          Changed_Date)
SELECT
    tf.Transaction_Flag_ID,
    tf.Transaction_ID,
    tf.Flag_ID,
    @lkpChangeSet      AS Change_Type_Lkp_ID,
    NULL               AS Previous_Is_Flagged,
    1                  AS New_Is_Flagged,
    NULL               AS Previous_Notes,
    NULL               AS New_Notes,
    NULL               AS Previous_Evidence_Document_ID,
    NULL               AS New_Evidence_Document_ID,
    @who               AS Changed_By,
    tf.Set_Date        AS Changed_Date
FROM   dbo.TmX_Transaction_Flag tf
WHERE  NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Transaction_Flag_History h
    WHERE  h.Transaction_Flag_ID = tf.Transaction_Flag_ID
      AND  h.Change_Type_Lkp_ID  = @lkpChangeSet
);

DECLARE @historyInserted int = @@ROWCOUNT;
PRINT CONCAT('Step C: ', @historyInserted, ' history row(s) inserted.');

-- -------------------------------------------------------------------------
-- Step D — Confirmation summary
-- -------------------------------------------------------------------------
PRINT '';
PRINT 'Backfill complete. Distribution:';

PRINT '';
PRINT 'Top 10 flags by transaction count (post-backfill):';
SELECT TOP 10
    c.Flag_Code,
    LEFT(c.Flag_Description, 100) + CASE WHEN LEN(c.Flag_Description) > 100 THEN '...' ELSE '' END AS Description_Preview,
    (SELECT COUNT(*) FROM dbo.TmX_Transaction_Flag tf
     WHERE tf.Flag_ID = c.Flag_ID AND tf.Is_Flagged = 1) AS Flagged_Transactions
FROM   dbo.TmX_Flag_Catalogue c
ORDER  BY (SELECT COUNT(*) FROM dbo.TmX_Transaction_Flag tf
           WHERE tf.Flag_ID = c.Flag_ID AND tf.Is_Flagged = 1) DESC;

PRINT '';
PRINT 'Totals:';
SELECT
    (SELECT COUNT(*) FROM dbo.TmX_Flag_Catalogue)             AS Catalogue_Rows,
    (SELECT COUNT(*) FROM dbo.TmX_Flag_Scope)                 AS Scope_Rows,
    (SELECT COUNT(*) FROM dbo.TmX_Transaction_Flag)           AS Transaction_Flag_Rows,
    (SELECT COUNT(*) FROM dbo.TmX_Transaction_Flag_History)   AS History_Rows;

PRINT '';
PRINT 'Orphan diagnostic — MRL keys present in UDF_Data with no matching';
PRINT 'TmX_Flag_Scope.Legacy_Field_Name (skipped silently by design):';

;WITH orphan_ticks AS (
    SELECT
        [key] COLLATE DATABASE_DEFAULT AS udf_key,
        td.Transaction_Id
    FROM   dbo.TmX_Transaction_Detail td
    CROSS  APPLY OPENJSON(td.UDF_Data)
    WHERE  td.UDF_Data IS NOT NULL
      AND  [key] COLLATE DATABASE_DEFAULT LIKE '%MRL%'
      AND  [value] COLLATE DATABASE_DEFAULT IN ('1', 'true')
)
SELECT
    udf_key                                                 AS orphan_key,
    COUNT(*)                                                AS skipped_tick_count
FROM   orphan_ticks o
WHERE  NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Flag_Scope s
    WHERE  s.Legacy_Field_Name = o.udf_key COLLATE DATABASE_DEFAULT
)
GROUP BY udf_key
ORDER BY skipped_tick_count DESC;

DROP TABLE #BackfillSet;
DROP TABLE #BackfillSet_Dedup;

GO

PRINT '';
PRINT 'Slice 8 Step 4 — transaction-flag backfill complete.';
PRINT 'Flag catalogue is now live. Next: EF entities + Application handlers + API endpoints (Slice 8 Step 5).';
