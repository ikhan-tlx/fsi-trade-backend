/* ============================================================================
   2026_08_065_RemediateManualFlagLeak.sql
   ----------------------------------------------------------------------------
   Fixes manual-flag indicators that still render as DYNAMIC FORM FIELDS in the
   Product Fields API (GET /api/v1/Product/{id}/Tabs/{tab}/Fields) instead of the
   manual-flag section.

   Root cause
     The original flag-catalogue migrations all keyed on Field_Name LIKE '%MRL%':
       - 2026_05_013  seeded TmX_Flag_Catalogue + TmX_Flag_Scope from
                      Field_Type_Lkp=28 AND Field_Name LIKE '%MRL%' rows.
       - 2026_05_025  (Step 2) deactivated Field_Name LIKE '%MRL%' rows.
       - GetTabFields / FormDefinition belt-and-braces also require '%MRL%'.
     The Guarantee sub-products name their manual-flag columns 'CG_MRF%' /
     'DG_MRF%' (Counter/Demand Guarantee, tabs 42/43), so every MRL-keyed step
     skipped them: they were never catalogued-scoped and never deactivated, and
     thus still come back as active fields. Their flag TEXT, however, already
     exists in TmX_Flag_Catalogue (seeded from the MRL-named twins on other
     products), so no new catalogue rows are needed.

   What this does (generic, not hard-coded to tabs 42/43)
     (1) For every ACTIVE checkbox_file (Field_Type_Lkp=28) row whose trimmed
         Field_Label matches an existing catalogue flag and that has no scope row
         yet, insert a TmX_Flag_Scope row  -> the flag now shows in the manual-
         flag section for that (product, sub-product tab), exactly like the
         Issuance/Amendment tabs already do.
     (2) Deactivate (Is_Active=0) those now-scoped field rows -> they stop
         leaking into the Fields API.
     (3) Deactivate residual dummy / section-header placeholder checkbox_file
         rows (labels NOT in the catalogue: 'dummy', 'Red Manual Flags', ...) —
         legacy form junk that also leaks as fields.

   Idempotent + re-runnable (NOT EXISTS guards; only flips Is_Active=1 rows).
   Rollback note at the bottom.

   Apply against ICBC_DEMO (or UAT) with sqlcmd.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required: TmX_Flag_* / TmX_Configuration_* carry filtered/computed indexes

DECLARE @who nvarchar(100) = N'System (2026_08_065 manual-flag leak remediation)';
DECLARE @now datetime      = GETUTCDATE();

/* ---- Snapshot the leaking REAL-flag rows ------------------------------------
   Active checkbox_file whose (trimmed) label is a known catalogue flag and that
   is not already scoped for this (Flag, Product, Tab, Legacy name). */
IF OBJECT_ID('tempdb..#Leak') IS NOT NULL DROP TABLE #Leak;
SELECT f.Tenant_Field_Setup_Id,
       f.Product_Id,
       f.Tab_Id,
       f.Field_Name,
       f.Field_Sequence,
       f.Visibility,
       c.Flag_ID
INTO   #Leak
FROM   dbo.TmX_Tenant_Field_Setup f
JOIN   dbo.TmX_Flag_Catalogue c
       ON c.Flag_Description = LTRIM(RTRIM(REPLACE(REPLACE(f.Field_Label, CHAR(13), ''), CHAR(10), '')))
WHERE  f.Is_Active      = 1
  AND  f.Field_Type_Lkp = 28
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Flag_Scope s
        WHERE  s.Flag_ID                     = c.Flag_ID
          AND  s.Product_ID                  = f.Product_Id
          AND  ISNULL(s.Tab_ID, -1)          = ISNULL(f.Tab_Id, -1)
          AND  ISNULL(s.Legacy_Field_Name,'')= ISNULL(f.Field_Name,''));

DECLARE @leakCount int;
SELECT  @leakCount = COUNT(*) FROM #Leak;
PRINT CONCAT('Leaking catalogued flag rows found: ', @leakCount);

/* ---- (1) Scope insert — surface them in the manual-flag section -------------
   The unique indexes on TmX_Flag_Scope (UQ_..._FlagProductTab /
   _FlagProduct_NoTab / _FlagAllProducts, created in 2026_06_031) key on
   (Flag_ID, Product_ID, Tab_ID) and do NOT include Legacy_Field_Name. So we must
   insert at most ONE scope row per (Flag, Product, Tab), and guard on that same
   triple — NOT on Legacy_Field_Name. When a sub-product tab has more than one
   active checkbox leaking the SAME catalogue flag (e.g. Guarantee CG_MRF/DG_MRF
   twins), we scope one representative (lowest Field_Sequence, then id) here and
   still deactivate ALL of them in step (2) below. (Keying on Legacy_Field_Name,
   as the original did, let two same-(Flag,Product,Tab) rows through the guard and
   hit the unique index — the UAT duplicate-key error.) */
;WITH leak_ranked AS (
    SELECT l.*,
           ROW_NUMBER() OVER (
               PARTITION BY l.Flag_ID, ISNULL(l.Product_Id, -1), ISNULL(l.Tab_Id, -1)
               ORDER BY l.Field_Sequence, l.Tenant_Field_Setup_Id) AS rn
    FROM   #Leak l )
INSERT INTO dbo.TmX_Flag_Scope
   (Flag_ID, Product_ID, Tab_ID, Sort_Order, Active_Flag, Legacy_Field_Name, Created_By, Created_Date)
SELECT z.Flag_ID,
       z.Product_Id,
       z.Tab_Id,
       ISNULL(z.Field_Sequence, 0),
       CASE WHEN z.Visibility = '0' THEN 0 ELSE 1 END,
       z.Field_Name,
       @who, @now
FROM   leak_ranked z
WHERE  z.rn = 1                                   -- one representative per (Flag, Product, Tab)
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Flag_Scope s
        WHERE  s.Flag_ID                  = z.Flag_ID
          AND  ISNULL(s.Product_ID, -1)   = ISNULL(z.Product_Id, -1)
          AND  ISNULL(s.Tab_ID, -1)       = ISNULL(z.Tab_Id, -1));   -- matches the unique index
PRINT CONCAT('(1) Scope rows inserted: ', @@ROWCOUNT);

/* ---- (2) Deactivate the now-scoped field rows ------------------------------ */
UPDATE f
SET    f.Is_Active = 0, f.Last_Updated_By = @who, f.Last_Updated_Date = @now
FROM   dbo.TmX_Tenant_Field_Setup f
JOIN   #Leak l ON l.Tenant_Field_Setup_Id = f.Tenant_Field_Setup_Id
WHERE  f.Is_Active = 1;
PRINT CONCAT('(2) Catalogued flag field rows deactivated: ', @@ROWCOUNT);

/* ---- (3) Deactivate residual dummy / section-header placeholder rows -------- */
UPDATE dbo.TmX_Tenant_Field_Setup
SET    Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
WHERE  Is_Active = 1
  AND  Field_Type_Lkp = 28
  AND  ( Field_Name LIKE '%dummy%'
      OR Field_Label IN (N'Red Manual Flags', N'Manual Red Flags', N'dummy') );
PRINT CONCAT('(3) Dummy/header placeholder rows deactivated: ', @@ROWCOUNT);

/* ---- Confirmation ---------------------------------------------------------- */
PRINT '';
PRINT 'Post-fix: remaining ACTIVE checkbox_file (Field_Type_Lkp=28) rows (should be 0, or only legit doc-uploads):';
SELECT f.Product_Id, f.Tab_Id, f.Field_Name, LEFT(f.Field_Label, 60) AS Label
FROM   dbo.TmX_Tenant_Field_Setup f
WHERE  f.Is_Active = 1 AND f.Field_Type_Lkp = 28
ORDER  BY f.Product_Id, f.Tab_Id;

PRINT '';
PRINT 'Post-fix: Product 6 flag-scope coverage by sub-product tab:';
SELECT s.Tab_ID, COUNT(*) AS ScopeRows, SUM(CAST(s.Active_Flag AS int)) AS ActiveScope
FROM   dbo.TmX_Flag_Scope s
WHERE  s.Product_ID = 6
GROUP  BY s.Tab_ID
ORDER  BY s.Tab_ID;

DROP TABLE #Leak;
PRINT '';
PRINT '2026_08_065_RemediateManualFlagLeak applied.';

/* ----------------------------------------------------------------------------
   ROLLBACK (manual): this migration is additive to TmX_Flag_Scope and flips
   Is_Active 1->0 on the affected field rows. To revert in a dev environment:
     UPDATE dbo.TmX_Tenant_Field_Setup SET Is_Active = 1
       WHERE Last_Updated_By = N'System (2026_08_065 manual-flag leak remediation)';
     DELETE FROM dbo.TmX_Flag_Scope
       WHERE Created_By = N'System (2026_08_065 manual-flag leak remediation)';
   ---------------------------------------------------------------------------- */
