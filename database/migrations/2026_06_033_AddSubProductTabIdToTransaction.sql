-- =========================================================================
-- 2026_06_033_AddSubProductTabIdToTransaction.sql
--
-- 2026-06-03 — add TmX_Transaction.Sub_Product_Tab_Id.
--
-- Stores the sub-type tab (TmX_Tab.Tab_Id) the maker selected for a Trade
-- transaction, captured at save time from the chosen ProductTab selector
-- checkbox. Drives the read-only "Transaction details" readout
-- (detailSections on GET /Transaction/{id}). NULL for KYC / single-tab
-- products and for legacy rows saved before this column existed — the read
-- side infers the tab from UDF_Data in those cases.
--
-- Idempotent: guarded by COL_LENGTH. Safe to re-run.
-- Transaction-wrapped with verification (pattern from 2026_05_025).
--
-- NOTE: the C# changes ship alongside this migration (Transaction.SubProductTabId
-- + config, population at create + value-save, detailSections in the detail
-- handler) — NOT applied by this script.
--
-- Run on: ICBC_DEMO
--   sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E -i "<path>\2026_06_033_AddSubProductTabIdToTransaction.sql"
-- =========================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY

IF COL_LENGTH('dbo.TmX_Transaction', 'Sub_Product_Tab_Id') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Transaction ADD Sub_Product_Tab_Id INT NULL;
    PRINT '1 : Column Sub_Product_Tab_Id added.';
END
ELSE PRINT '1 : Column Sub_Product_Tab_Id already exists -- skipped.';

-- ============================================================
-- VERIFICATION
-- ============================================================
SELECT step_name, result FROM (
    SELECT 1 AS ord, 'Sub_Product_Tab_Id column exists' AS step_name,
        CASE WHEN COL_LENGTH('dbo.TmX_Transaction','Sub_Product_Tab_Id') IS NOT NULL
             THEN 'PASS' ELSE 'FAIL' END AS result
) AS v(ord, step_name, result)
ORDER BY ord;

COMMIT TRANSACTION;
PRINT '================================================';
PRINT 'Migration 2026_06_033 completed successfully.';
PRINT '================================================';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'ERROR -- migration rolled back.';
    PRINT 'Message : ' + ERROR_MESSAGE();
    PRINT 'Line    : ' + CAST(ERROR_LINE() AS VARCHAR);
    THROW;
END CATCH;
