-- =========================================================================
-- 2026_06_032_DropFlagCatalogueCategory.sql
--
-- Slice 8.2 — drop the flag-level category from the catalogue.
--
-- Driven by BE_revamp_Changes.md ("Manual Red Flag — drop the category
-- column", 2026-06-01). Categorisation now lives on the flag SCOPE
-- (product / tab a flag is deployed to), so a catalogue-level category is
-- redundant. The FE has already stopped sending flagCategoryLkpId on
-- POST/PUT /api/v1/Flag.
--
-- Downstream check (done before writing this migration): the only readers
-- of Flag_Category_Lkp_ID were the flag DTOs (list / detail), the
-- per-transaction flag projection, the TopFlagged stats row, and the
-- create-flow code-prefix generator. All have been updated to no longer
-- reference it. The FLAG_CATEGORY rows in TmX_Lookup (seeded by
-- 2026_05_012) are left in place — harmless, just unused.
--
-- What this migration does
-- ------------------------
--   1. Drop IX_TmX_Flag_Catalogue_Category_Type (keys on the column).
--   2. Drop column Flag_Category_Lkp_ID from TmX_Flag_Catalogue.
--   3. Recreate a lean IX_TmX_Flag_Catalogue_Type on (Flag_Type_Lkp_ID)
--      to preserve the active-type filter path the composite index used
--      to cover.
--
-- Idempotent: every step is guarded; safe to re-run.
-- Transaction-wrapped with verification (pattern from 2026_05_025).
--
-- ONE-WAY NOTE: re-running the earlier seed 2026_05_013 after this
-- migration would fail (it inserts into the now-dropped column). Migrations
-- are applied in sequence once; this is the expected forward-only effect of
-- a column drop.
--
-- Run on: ICBC_DEMO
--   sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E -i "<path>\2026_06_032_DropFlagCatalogueCategory.sql"
-- =========================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY

-- ============================================================
-- 1. Drop the composite index that keys on Flag_Category_Lkp_ID.
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'IX_TmX_Flag_Catalogue_Category_Type'
             AND object_id = OBJECT_ID('dbo.TmX_Flag_Catalogue'))
BEGIN
    DROP INDEX [IX_TmX_Flag_Catalogue_Category_Type] ON dbo.TmX_Flag_Catalogue;
    PRINT '1 : IX_TmX_Flag_Catalogue_Category_Type dropped.';
END
ELSE PRINT '1 : IX_TmX_Flag_Catalogue_Category_Type not present -- skipped.';

-- ============================================================
-- 2. Drop the column.
-- ============================================================
IF COL_LENGTH('dbo.TmX_Flag_Catalogue', 'Flag_Category_Lkp_ID') IS NOT NULL
BEGIN
    ALTER TABLE dbo.TmX_Flag_Catalogue DROP COLUMN [Flag_Category_Lkp_ID];
    PRINT '2 : Column Flag_Category_Lkp_ID dropped.';
END
ELSE PRINT '2 : Column Flag_Category_Lkp_ID already gone -- skipped.';

-- ============================================================
-- 3. Recreate a lean active-type index (the surviving half of the
--    dropped composite index).
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_TmX_Flag_Catalogue_Type'
                 AND object_id = OBJECT_ID('dbo.TmX_Flag_Catalogue'))
BEGIN
    CREATE INDEX [IX_TmX_Flag_Catalogue_Type]
        ON dbo.TmX_Flag_Catalogue ([Flag_Type_Lkp_ID])
        WHERE [Active_Flag] = 1;
    PRINT '3 : IX_TmX_Flag_Catalogue_Type created.';
END
ELSE PRINT '3 : IX_TmX_Flag_Catalogue_Type already exists -- skipped.';

-- ============================================================
-- VERIFICATION
-- ============================================================
SELECT step_name, result FROM (
    SELECT 1 AS ord, 'Flag_Category_Lkp_ID column removed' AS step_name,
        CASE WHEN COL_LENGTH('dbo.TmX_Flag_Catalogue','Flag_Category_Lkp_ID') IS NULL
             THEN 'PASS' ELSE 'FAIL' END AS result
    UNION ALL SELECT 2, 'Composite category index removed',
        CASE WHEN NOT EXISTS (SELECT 1 FROM sys.indexes
              WHERE name = 'IX_TmX_Flag_Catalogue_Category_Type'
                AND object_id = OBJECT_ID('dbo.TmX_Flag_Catalogue'))
             THEN 'PASS' ELSE 'FAIL' END
    UNION ALL SELECT 3, 'Lean type index present',
        CASE WHEN EXISTS (SELECT 1 FROM sys.indexes
              WHERE name = 'IX_TmX_Flag_Catalogue_Type'
                AND object_id = OBJECT_ID('dbo.TmX_Flag_Catalogue'))
             THEN 'PASS' ELSE 'FAIL' END
) AS v(ord, step_name, result)
ORDER BY ord;

COMMIT TRANSACTION;
PRINT '================================================';
PRINT 'Migration 2026_06_032 completed successfully.';
PRINT '================================================';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'ERROR -- migration rolled back.';
    PRINT 'Message : ' + ERROR_MESSAGE();
    PRINT 'Line    : ' + CAST(ERROR_LINE() AS VARCHAR);
    THROW;
END CATCH;
