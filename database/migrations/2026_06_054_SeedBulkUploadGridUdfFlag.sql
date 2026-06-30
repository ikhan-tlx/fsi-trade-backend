-- =========================================================================
-- 2026_06_054_SeedBulkUploadGridUdfFlag.sql
--
-- 2026-06-23 — enable the dynamic-grid "bulk upload + template download"
-- feature on the trade goods-detail grids.
--
-- Mechanism (no schema change): the feature is config-flagged per grid via
-- the existing per-field JSON blob dbo.TmX_Tenant_Field_Setup.UDF_Data
-- (already surfaced verbatim on every form-schema read endpoint as
-- `udfData` — see GetTabFieldsQueryHandler / GetProductFormDefinitionQuery-
-- Handler). The FE TableField reads `udfData.bulkUpload`; when truthy it
-- renders the Download-template + Bulk-upload toolbar. Any grid can opt in
-- by setting this flag — fully generic.
--
-- Target set (derived, not hard-coded): every TABLE field (Field_Type_Lkp =
-- 27) that owns an HS-Code-List dropdown child column, i.e. a child whose
-- Field_Lookup_Type references 'HSCodeList'. That is every goods grid that
-- accepts an HS code (21 today) — a superset of the grids that also carry the
-- dependent HS-Code-Price view-only column. Keyed on the HS-code column (not
-- the price column) so grids without a price column are still covered.
--
-- 2026-06-23 (rev): widened from the HSPriceList-child criterion (which only
-- matched 16 grids) to the HSCodeList-child criterion (all 21), so EVERY grid
-- that takes an HS code gets the template/bulk-upload toolbar.
--
-- Idempotent: merges {"bulkUpload":true} into any existing UDF_Data JSON
-- (e.g. preserves {"TabId":N}); sets it outright when null/blank. Safe to
-- re-run.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @who nvarchar(100) = N'System (enable grid bulk upload)';
DECLARE @now datetime      = GETUTCDATE();

-- Resolve the target grid table fields.
IF OBJECT_ID('tempdb..#GridFields') IS NOT NULL DROP TABLE #GridFields;

SELECT DISTINCT f.Tenant_Field_Setup_Id
INTO   #GridFields
FROM   dbo.TmX_Tenant_Field_Setup f
WHERE  f.Field_Type_Lkp = 27
  AND  EXISTS (
        SELECT 1
        FROM   dbo.TmX_Tenant_Field_Setup c
        WHERE  c.Parent_Tenant_Field_Setup_Id = f.Tenant_Field_Setup_Id
          AND  c.Field_Lookup_Type LIKE '%HSCodeList%'
       );

DECLARE @targets int = (SELECT COUNT(*) FROM #GridFields);
PRINT CONCAT('Bulk-upload flag: matched ', @targets, ' goods-detail grid field(s).');

IF @targets = 0
BEGIN
    PRINT 'No matching grids found — nothing to do.';
    RETURN;
END

-- Merge / set the flag.
UPDATE f
SET    f.UDF_Data =
           CASE
               WHEN f.UDF_Data IS NULL OR LTRIM(RTRIM(f.UDF_Data)) = ''
                    THEN N'{"bulkUpload":true}'
               ELSE JSON_MODIFY(f.UDF_Data, '$.bulkUpload', CAST(1 AS bit))
           END,
       f.Last_Updated_By   = @who,
       f.Last_Updated_Date = @now
FROM   dbo.TmX_Tenant_Field_Setup f
JOIN   #GridFields g ON g.Tenant_Field_Setup_Id = f.Tenant_Field_Setup_Id;

PRINT CONCAT('Bulk-upload flag: updated ', @@ROWCOUNT, ' row(s).');

PRINT '';
PRINT 'Post-state:';
SELECT f.Tenant_Field_Setup_Id, f.Field_Name, f.Field_Type_Lkp, f.UDF_Data
FROM   dbo.TmX_Tenant_Field_Setup f
JOIN   #GridFields g ON g.Tenant_Field_Setup_Id = f.Tenant_Field_Setup_Id
ORDER  BY f.Tenant_Field_Setup_Id;

DROP TABLE #GridFields;
GO

PRINT 'Bulk-upload grid UDF flag seed complete.';
