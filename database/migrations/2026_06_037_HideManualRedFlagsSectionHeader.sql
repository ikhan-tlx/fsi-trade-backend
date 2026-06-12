-- =========================================================================
-- 2026_06_037_HideManualRedFlagsSectionHeader.sql
--
-- 2026-06-05 — hide the "Manual Red Flags" dynamic-form section header.
--
-- The dynamic form carries a divider/section-header field (Field_Name like
-- "%Dummy", Field_Label = 'Manual Red Flags') that renders an empty
-- "MANUAL RED FLAGS" heading in the transaction form. The actual flags live in
-- the right-hand Manual Flags rail, so this in-form heading is redundant and
-- should not be shown. Deactivating it (Is_Active = 0) removes it from the
-- form server-side — the field endpoints (/FormDefinition, /Tabs/{id}/Fields)
-- already exclude Is_Active = 0 rows, so the FE never receives it.
--
-- Scope: ONLY rows whose Field_Label is exactly 'Manual Red Flags' (one per
-- product variant); other "Dummy" section headers (sub-product titles) are
-- left untouched.
--
-- Idempotent: re-running only re-sets already-hidden rows (no-op). The original
-- Is_Active values can be restored with the rollback note at the bottom.
-- Transaction-wrapped with verification.
--
-- Run on: ICBC_DEMO
--   sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E -i "<path>\2026_06_037_HideManualRedFlagsSectionHeader.sql"
-- =========================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY

DECLARE @affected INT;

UPDATE dbo.TmX_Tenant_Field_Setup
SET Is_Active         = 0,
    Last_Updated_By   = 'mig-2026_06_037',
    Last_Updated_Date = GETUTCDATE()
WHERE Field_Label = 'Manual Red Flags'
  AND Is_Active <> 0;

SET @affected = @@ROWCOUNT;
PRINT '1 : Hid ' + CAST(@affected AS VARCHAR) + ' "Manual Red Flags" header row(s).';

-- ============================================================
-- VERIFICATION
-- ============================================================
SELECT step_name, result FROM (
    SELECT 1 AS ord, 'No active "Manual Red Flags" header remains' AS step_name,
        CASE WHEN NOT EXISTS (
                SELECT 1 FROM dbo.TmX_Tenant_Field_Setup
                WHERE Field_Label = 'Manual Red Flags' AND Is_Active <> 0)
             THEN 'PASS' ELSE 'FAIL' END AS result
) AS v(ord, step_name, result)
ORDER BY ord;

COMMIT TRANSACTION;
PRINT '================================================';
PRINT 'Migration 2026_06_037 completed successfully.';
PRINT '================================================';

-- Rollback (manual): re-activate the header(s) if ever needed —
--   UPDATE dbo.TmX_Tenant_Field_Setup SET Is_Active = 1
--   WHERE Field_Label = 'Manual Red Flags';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'ERROR -- migration rolled back.';
    PRINT 'Message : ' + ERROR_MESSAGE();
    PRINT 'Line    : ' + CAST(ERROR_LINE() AS VARCHAR);
    THROW;
END CATCH;
