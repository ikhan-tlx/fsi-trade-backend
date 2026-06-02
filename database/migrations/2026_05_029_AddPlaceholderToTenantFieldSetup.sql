-- =========================================================================
-- 2026_05_029_AddPlaceholderToTenantFieldSetup.sql
--
-- Slice 12 §A — adds Placeholder column to TmX_Tenant_Field_Setup.
--
-- Driven by the Field Configuration admin page (BE_revamp_Changes.md §10.2),
-- which lets admins edit a per-field hint string that the FE renders under
-- the field label on every form. The FE wizard / transaction edit page
-- must also start consuming this column when rendering fields (see the
-- 2026-05-29 FE_CHANGES_REQUIRED.md entry).
--
-- Not localised for now — single string per field. If per-culture
-- placeholders become a real need later, add a Locale_Placeholder column
-- alongside Locale_Label.
--
-- Idempotent: column add guarded by sys.columns lookup. Safe to re-run.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.TmX_Tenant_Field_Setup')
      AND  name      = N'Placeholder'
)
BEGIN
    ALTER TABLE [dbo].[TmX_Tenant_Field_Setup]
        ADD [Placeholder] [nvarchar](500) NULL;
    PRINT '  Added Placeholder column to TmX_Tenant_Field_Setup.';
END
ELSE
    PRINT '  Placeholder column already present — skipped.';
GO

PRINT 'Migration 2026_05_029_AddPlaceholderToTenantFieldSetup complete.';
GO
