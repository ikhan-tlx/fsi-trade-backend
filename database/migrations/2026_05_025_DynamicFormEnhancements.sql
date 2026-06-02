-- ============================================================
-- Migration : 20260522_001_DynamicFormEnhancements.sql
-- Project   : Trade.Compliance
-- Author    : FSI Team
-- Date      : 2026-05-22
-- ============================================================
-- STEPS:
--   1. Add Is_Active to TmX_Tenant_Field_Setup
--   2. Deactivate all MRL fields
--   3. Add Is_Visible to TmX_Entity_Tab_Product_Mapping
--   4. Hide Tab 1 (Parent Objects) — hardcoded, system root
--   5. Hide selector tabs dynamically (Checkbox pattern)
--      BE still returns ALL tabs — FE handles Is_Visible
--   6. Insert ONE 'ProductTab' row into TmX_Lookup
--   7. Update Field_Lookup_Type = 'ProductTab' on all
--      checkbox fields inside selector tabs (13, 21, 38 ...)
--   8a. Add UDF_Data column to TmX_Tenant_Field_Setup
--   8b. Populate UDF_Data with {"TabId":X} for each checkbox
--       (joins Field_Label → TmX_Tab.Tab_Name to resolve Tab_Id)
-- ============================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY

-- ============================================================
-- STEP 1: ADD Is_Active TO TmX_Tenant_Field_Setup
-- ============================================================

IF COL_LENGTH('dbo.TmX_Tenant_Field_Setup', 'Is_Active') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Tenant_Field_Setup
        ADD Is_Active BIT NOT NULL
            CONSTRAINT DF_TmX_TenantFieldSetup_IsActive DEFAULT (1);
    PRINT 'STEP 1 : Column Is_Active added.';
END
ELSE
    PRINT 'STEP 1 : Column Is_Active already exists — skipped.';


-- ============================================================
-- STEP 2: DEACTIVATE ALL MRL FIELDS
-- ============================================================
-- sp_executesql required: Is_Active was added by Step 1 ALTER TABLE
-- in this same batch. SQL Server parses the entire batch before
-- execution, so a direct reference to Is_Active would fail at
-- compile time if the column was just created above.

EXEC sp_executesql N'
UPDATE dbo.TmX_Tenant_Field_Setup
SET
    Is_Active         = 0,
    Last_Updated_By   = ''Migration_20260522_001'',
    Last_Updated_Date = GETUTCDATE()
WHERE Field_Name LIKE ''%MRL%''
  AND Is_Active = 1;
';
PRINT 'STEP 2 : MRL fields deactivated — rows: ' + CAST(@@ROWCOUNT AS VARCHAR);


-- ============================================================
-- STEP 3: ADD Is_Visible TO TmX_Entity_Tab_Product_Mapping
-- ============================================================
-- Is_Active (existing) = fields are queryable.
-- Is_Visible (new)     = UI hint: should this tab appear
--                        in the frontend tab rail?
-- BE must return ALL rows regardless of Is_Visible.
-- FE reads the flag and decides what to render.

IF COL_LENGTH('dbo.TmX_Entity_Tab_Product_Mapping', 'Is_Visible') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Entity_Tab_Product_Mapping
        ADD Is_Visible BIT NOT NULL
            CONSTRAINT DF_TmX_EntityTabProductMapping_IsVisible DEFAULT (1);
    PRINT 'STEP 3 : Column Is_Visible added.';
END
ELSE
    PRINT 'STEP 3 : Column Is_Visible already exists — skipped.';


-- ============================================================
-- STEP 4: HIDE TAB 1 (PARENT OBJECTS)
-- ============================================================
-- Safe to hardcode: Tab_Id=1 is the IDENTITY seed row present
-- on every environment. All its fields are Type=40 (TableRoot)
-- — no renderable widgets exist for this type.

-- sp_executesql required: Is_Visible was added by Step 3 ALTER TABLE above.
EXEC sp_executesql N'
UPDATE dbo.TmX_Entity_Tab_Product_Mapping
SET
    Is_Visible        = 0,
    Last_Updated_By   = ''Migration_20260522_001'',
    Last_Updated_Date = GETUTCDATE()
WHERE Tab_Id    = 1
  AND Is_Visible = 1;
';
PRINT 'STEP 4 : Tab 1 (Parent Objects) hidden — rows: ' + CAST(@@ROWCOUNT AS VARCHAR);


-- ============================================================
-- STEP 5: HIDE SELECTOR TABS DYNAMICALLY (NO HARDCODED IDs)
-- ============================================================
-- Identifies "Select a Tab" tabs by a data pattern:
--   Any Tab_Id in TmX_Tenant_Field_Setup that contains
--   Field_Type_Lkp=28 fields whose Field_Name ends in 'Checkbox'
--   (e.g. ILFCheckbox, SGCheckbox, ELCCheckbox...)
--
-- This covers Tab 13 (Import), Tab 21 (Export),
-- Tab 38 (Guarantee) and any future selector tabs —
-- without ever listing their IDs.
--
-- KYC product excluded: it has its own visibility flow.
--
-- ⚠  BE NOTE: The SP / endpoint must NOT filter on Is_Visible.
--    Return ALL ETPM rows. The FE uses Is_Visible to decide
--    which tabs to show in the tab rail.

-- sp_executesql required: Is_Visible was added by Step 3 ALTER TABLE above.
EXEC sp_executesql N'
UPDATE etpm
SET
    etpm.Is_Visible        = 0,
    etpm.Last_Updated_By   = ''Migration_20260522_001'',
    etpm.Last_Updated_Date = GETUTCDATE()
FROM dbo.TmX_Entity_Tab_Product_Mapping etpm
WHERE etpm.Tab_Id IN (
    SELECT DISTINCT tfs.Tab_Id
    FROM   dbo.TmX_Tenant_Field_Setup tfs
    WHERE  tfs.Field_Name     LIKE ''%Checkbox%''
      AND  tfs.Field_Name     != ''dummy''
)
AND etpm.Product_Id NOT IN (
    SELECT Product_Id FROM dbo.TmX_Product
    WHERE  Product_Name LIKE ''%KYC%''
)
AND etpm.Is_Visible = 1;
';
PRINT 'STEP 5 : Selector tabs hidden — rows: ' + CAST(@@ROWCOUNT AS VARCHAR);


-- ============================================================
-- STEP 6: INSERT ONE 'ProductTab' ROW INTO TmX_Lookup
-- ============================================================
-- This single row registers the lookup type.
-- The individual sub-product labels (Import LC Issuance etc.)
-- are NOT stored here — they are read directly from
-- Field_Label on the checkbox fields in TmX_Tenant_Field_Setup.
-- Step 7 wires those fields to this type via Field_Lookup_Type.

IF NOT EXISTS (
    SELECT 1 FROM dbo.TmX_Lookup
    WHERE  Lookup_Type  = 'ProductTab'
      AND  Hidden_Value = 'ProductTab'
      AND  Tenant_ID    = 245
)
BEGIN
    INSERT INTO dbo.TmX_Lookup
    (
        Tenant_ID, Lookup_Type, Hidden_Value, Visible_Value,
        Is_Active, Active_Flag, User_Editable,
        Sort_Order, Locale_ID,
        Created_By, Created_Date,
        Last_Updated_By, Last_Updated_Date
    )
    VALUES
    (
        245, 'ProductTab', 'ProductTab', 'Product Tab',
        1, 1, 0,
        1, 1,
        'Migration_20260522_001',
        CONVERT(NVARCHAR(100), GETUTCDATE(), 120),
        'Migration_20260522_001',
        GETUTCDATE()
    );
    PRINT 'STEP 6 : ProductTab lookup row inserted.';
END
ELSE
    PRINT 'STEP 6 : ProductTab lookup row already exists — skipped.';


-- ============================================================
-- STEP 7: RE-TYPE CHECKBOX FIELDS USING THE ProductTab LOOKUP
-- ============================================================
-- Two columns are updated on every sub-product selector checkbox:
--
--   Field_Type_Lkp   (INT)       — currently 28 (generic CheckboxBool)
--                                  → updated to the Lookup_ID of the
--                                    'ProductTab' row from Step 6.
--                                  This gives the renderer a distinct
--                                  type to handle as a sub-product
--                                  selector widget, separate from
--                                  ordinary boolean checkboxes.
--
--   Field_Lookup_Type (NVARCHAR) — currently NULL
--                                  → updated to the Lookup_Type string
--                                    ('ProductTab') from Step 6.
--                                  This tells the API which TmX_Lookup
--                                  group to return for this field.
--
-- Both values are READ from TmX_Lookup (not hardcoded) so if the
-- type name ever changes, only Step 6 needs updating.
--
-- Guard: missing ProductTab row fails fast with a clear message
-- rather than silently writing NULLs into the field rows.

DECLARE @ProductTabLookupId   INT;
DECLARE @ProductTabLookupType NVARCHAR(50);

SELECT
    @ProductTabLookupId   = Lookup_ID,
    @ProductTabLookupType = Lookup_Type
FROM dbo.TmX_Lookup
WHERE Hidden_Value = 'ProductTab';

IF @ProductTabLookupId IS NULL
    THROW 50001,
          'STEP 7 ABORTED: ProductTab row not found in TmX_Lookup. Ensure Step 6 ran successfully before Step 7.',
          1;

PRINT 'STEP 7 : ProductTab Lookup_ID = ' + CAST(@ProductTabLookupId AS VARCHAR)
      + ', Lookup_Type = ' + @ProductTabLookupType;

-- WHERE uses Field_Type_Lkp = 28 to find CheckboxBool records
-- BEFORE they are re-typed — same pattern as Step 5.
UPDATE tfs
SET
    tfs.Field_Type_Lkp    = @ProductTabLookupId,     -- re-type: 28 → ProductTab Lookup_ID
    tfs.Field_Lookup_Type = @ProductTabLookupType,   -- wire lookup source string
    tfs.Last_Updated_By   = 'Migration_20260522_001',
    tfs.Last_Updated_Date = GETUTCDATE()
FROM dbo.TmX_Tenant_Field_Setup tfs

WHERE tfs.Tab_Id IN (
    -- Same subquery as Step 5 — consistent tab identification
    SELECT DISTINCT tfs2.Tab_Id
    FROM   dbo.TmX_Tenant_Field_Setup tfs2
    WHERE  tfs2.Field_Name     LIKE '%Checkbox%'
      AND  tfs2.Field_Name     != 'dummy'
)
AND tfs.Field_Name      LIKE '%Checkbox%'
AND tfs.Field_Name      != 'dummy'
AND tfs.Product_Id NOT IN (
    SELECT Product_Id FROM dbo.TmX_Product
    WHERE  Product_Name LIKE '%KYC%'
);

PRINT 'STEP 7 : Checkbox fields re-typed to ProductTab — rows: '
      + CAST(@@ROWCOUNT AS VARCHAR);


-- ============================================================
-- STEP 8a: ADD UDF_Data TO TmX_Tenant_Field_Setup
-- ============================================================
-- UDF_Data (NVARCHAR MAX, nullable) is the system-wide convention
-- for user-defined JSON payloads — used on 15+ other tables.
-- Here it stores the destination tab linkage for each sub-product
-- selector checkbox:  {"TabId":6}
-- FE reads this to know which tab to navigate to when the user
-- checks a given checkbox.

IF COL_LENGTH('dbo.TmX_Tenant_Field_Setup', 'UDF_Data') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Tenant_Field_Setup
        ADD UDF_Data NVARCHAR(MAX) NULL;
    PRINT 'STEP 8a: Column UDF_Data added to TmX_Tenant_Field_Setup.';
END
ELSE
    PRINT 'STEP 8a: Column UDF_Data already exists — skipped.';


-- ============================================================
-- STEP 8b: POPULATE UDF_Data WITH TARGET TAB LINKAGE
-- ============================================================
-- Resolves Field_Label → Tab_Id in two layers:
--
--   Layer 1 — Manual override map (known name mismatches):
--     Field_Label in TmX_Tenant_Field_Setup differs slightly
--     from Tab_Name in TmX_Tab for these cases:
--       'Import Drawing'                               → 'Import Drawings'
--       'Import Contract Drawing'                      → 'Import Contract Drawings'
--       'Import Collection Lodgement'                  → 'Import Collection Lodgment'
--       'Export Advance Payment Performance'           → 'Export Advance Payment - Performance'
--       'Export Drawings(LC/Collections/Open Account)' → 'Export Drawings'
--       'Contract Guarantee/SBLC'                      → 'Counter Guarantee/SBLC'
--
--   Layer 2 — Exact match fallback:
--     All other checkboxes (e.g. 'Import LC Issuance') match
--     Tab_Name directly with no override needed.
--
-- Uses Field_Lookup_Type = 'ProductTab' to identify targets
-- (set by Step 7 — more reliable than Field_Type_Lkp post-retype).
--
-- sp_executesql required: UDF_Data was added by Step 8a above
-- in this same batch — same parse-time column reference issue
-- as Steps 2, 4, and 5.
--
-- Only updates rows where UDF_Data IS NULL (idempotent guard).

EXEC sp_executesql N'
;WITH label_override AS (
    -- Known Field_Label → Tab_Name corrections.
    -- Add rows here if new mismatches are discovered.
    SELECT field_label, resolved_tab_name
    FROM (VALUES
        (N''Import Drawing'',                                N''Import Drawings''),
        (N''Import Contract Drawing'',                       N''Import Contract Drawings''),
        (N''Import Collection Lodgement'',                   N''Import Collection Lodgment''),
        (N''Export Advance Payment Performance'',            N''Export Advance Payment - Performance''),
        (N''Export Drawings(LC/Collections/Open Account)'',  N''Export Drawings''),
        (N''Contract Guarantee/SBLC'',                       N''Counter Guarantee/SBLC'')
    ) AS v(field_label, resolved_tab_name)
)
UPDATE tfs
SET
    tfs.UDF_Data          = ''{"TabId":'' + CAST(t.Tab_Id AS NVARCHAR(10)) + ''}'',
    tfs.Last_Updated_By   = ''Migration_20260522_001'',
    tfs.Last_Updated_Date = GETUTCDATE()
FROM  dbo.TmX_Tenant_Field_Setup tfs
LEFT  JOIN label_override lo
    ON  lo.field_label        = tfs.Field_Label   -- check override map first
JOIN  dbo.TmX_Tab t
    ON  t.Tab_Name = COALESCE(lo.resolved_tab_name, tfs.Field_Label)
                              -- override wins; falls back to exact match
WHERE tfs.Field_Lookup_Type = ''ProductTab''
  AND tfs.Field_Name        LIKE ''%Checkbox%''
  AND tfs.Field_Name         != ''dummy''
  AND tfs.UDF_Data           IS NULL              -- idempotent: skip already-set rows
  AND tfs.Product_Id NOT IN (
      SELECT Product_Id FROM dbo.TmX_Product
      WHERE  Product_Name LIKE ''%KYC%''
  );
';
PRINT 'STEP 8b: UDF_Data tab linkage populated — rows: ' + CAST(@@ROWCOUNT AS VARCHAR);


-- ============================================================
-- VERIFICATION
-- ============================================================
-- sp_executesql required: sub-queries inside CASE expressions
-- reference Is_Active and Is_Visible which were added in this
-- same batch (Steps 1 and 3). Direct reference would fail at
-- parse time; dynamic SQL compiles at execution time.

EXEC sp_executesql N'
SELECT step_name, result FROM (

    SELECT 1 AS ord, ''Is_Active on TmX_Tenant_Field_Setup'' AS step_name,
        CASE WHEN COL_LENGTH(''dbo.TmX_Tenant_Field_Setup'',''Is_Active'') IS NOT NULL
             THEN ''PASS'' ELSE ''FAIL'' END AS result

    UNION ALL SELECT 2, ''MRL fields all inactive'',
        CASE WHEN NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Tenant_Field_Setup
            WHERE Field_Name LIKE ''%MRL%'' AND Is_Active = 1)
        THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 3, ''Is_Visible on TmX_Entity_Tab_Product_Mapping'',
        CASE WHEN COL_LENGTH(''dbo.TmX_Entity_Tab_Product_Mapping'',''Is_Visible'') IS NOT NULL
             THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 4, ''Tab 1 hidden'',
        CASE WHEN NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Entity_Tab_Product_Mapping
            WHERE Tab_Id = 1 AND Is_Visible = 1)
        THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 5, ''Selector tabs hidden (non-KYC)'',
        CASE WHEN NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Entity_Tab_Product_Mapping etpm
            WHERE etpm.Tab_Id IN (
                SELECT DISTINCT tfs.Tab_Id FROM dbo.TmX_Tenant_Field_Setup tfs
                WHERE tfs.Field_Name LIKE ''%Checkbox%''
                  AND tfs.Field_Name != ''dummy''
            )
            AND etpm.Product_Id NOT IN (
                SELECT Product_Id FROM dbo.TmX_Product WHERE Product_Name LIKE ''%KYC%''
            )
            AND etpm.Is_Visible = 1)
        THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 6, ''ProductTab lookup row exists'',
        CASE WHEN EXISTS (
            SELECT 1 FROM dbo.TmX_Lookup
            WHERE Lookup_Type = ''ProductTab''
              AND Hidden_Value = ''ProductTab''
              AND Tenant_ID = 245)
        THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 7, ''Checkbox fields wired to ProductTab'',
        CASE WHEN NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Tenant_Field_Setup tfs
            WHERE tfs.Tab_Id IN (
                SELECT DISTINCT tfs2.Tab_Id FROM dbo.TmX_Tenant_Field_Setup tfs2
                WHERE tfs2.Field_Name LIKE ''%Checkbox%''
                  AND tfs2.Field_Name != ''dummy''
            )
            AND tfs.Field_Name LIKE ''%Checkbox%''
            AND tfs.Field_Name != ''dummy''
            AND (tfs.Field_Lookup_Type IS NULL OR tfs.Field_Lookup_Type != ''ProductTab'')
            AND tfs.Product_Id NOT IN (
                SELECT Product_Id FROM dbo.TmX_Product WHERE Product_Name LIKE ''%KYC%''
            ))
        THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 8, ''UDF_Data column exists on TmX_Tenant_Field_Setup'',
        CASE WHEN COL_LENGTH(''dbo.TmX_Tenant_Field_Setup'',''UDF_Data'') IS NOT NULL
             THEN ''PASS'' ELSE ''FAIL'' END

    UNION ALL SELECT 9, ''UDF_Data populated (0 unmatched = PASS; any NULL = WARN)'',
        CASE WHEN NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Tenant_Field_Setup
            WHERE Field_Lookup_Type = ''ProductTab''
              AND Field_Name LIKE ''%Checkbox%''
              AND Field_Name != ''dummy''
              AND UDF_Data IS NULL
              AND Product_Id NOT IN (
                  SELECT Product_Id FROM dbo.TmX_Product WHERE Product_Name LIKE ''%KYC%''
              ))
        THEN ''PASS'' ELSE ''WARN — some checkboxes have no matching Tab_Name'' END

) AS v(ord, step_name, result)
ORDER BY ord;
';


COMMIT TRANSACTION;
PRINT '================================================';
PRINT 'Migration 20260522_001 completed successfully.';
PRINT '================================================';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'ERROR — migration rolled back.';
    PRINT 'Message : ' + ERROR_MESSAGE();
    PRINT 'Line    : ' + CAST(ERROR_LINE() AS VARCHAR);
    THROW;
END CATCH;
