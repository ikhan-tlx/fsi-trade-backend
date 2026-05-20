-- =========================================================================
-- 2026_05_013_SeedFlagCatalogueFromTenantFieldSetup.sql
--
-- Slice 8 Step 3 — populate TmX_Flag_Catalogue + TmX_Flag_Scope from the
-- existing dynamic-form definitions.
--
-- Source rows
--   dbo.TmX_Tenant_Field_Setup where:
--     Field_Type_Lkp   = 28                       (checkbox_file)
--     Field_Name       LIKE '%MRL%'                (Manual Red flag)
--     Field_Table_Name = 'TmxTransactionDetail[]'  (real form fields —
--                                                   excludes 114 "Dummy"
--                                                   placeholder rows
--                                                   the legacy form
--                                                   designer left behind)
--
--   Net: 488 rows across Products 2 (Import), 3 (Export), 5 (KYC),
--   6 (Letter of Guarantee).
--
-- Output
--   TmX_Flag_Catalogue   ~25-40 rows — one per distinct trimmed
--                        Field_Label (the indicator text). Same flag
--                        text living on multiple product-prefixed
--                        columns collapses into a single catalogue
--                        entry.
--
--   TmX_Flag_Scope       488 rows — one per source row, keyed by
--                        (Flag_ID, Product_ID, Tab_ID).
--                        Active_Flag = 0 where source Visibility = '0'.
--                        Legacy_Field_Name preserves the original
--                        "ILFMRL5"-style identifier for traceability and
--                        for the Step 4 backfill to resolve JSON keys.
--
-- Category assignment
--   Product 5 (KYC)  → FLAG_CATEGORY = 'KYC'
--   Everything else  → FLAG_CATEGORY = 'TBML'
--
-- Type / Severity / Weight
--   All MRL rows are Manual flags (the M in MRL).
--   Default severity is 'Medium' for everything (admin can tune later).
--   Default_Weight stays at the catalogue default (1.00).
--   Requires_Evidence = 1 because the source Field_Type_Lkp = 28
--   (checkbox_file).
--
-- Idempotency
--   Catalogue insert: NOT EXISTS on (Flag_Code).
--   Scope insert: NOT EXISTS on (Flag_ID, Product_ID, Tab_ID, Legacy_Field_Name).
--
-- Pre-conditions
--   • 2026_05_011 has run (Flag tables exist).
--   • 2026_05_012 has run (FLAG_TYPE / FLAG_CATEGORY / FLAG_SEVERITY
--     lookup rows exist).
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @now            datetime      = GETUTCDATE();
DECLARE @who            nvarchar(100) = N'System (Slice 8 catalogue seed)';

-- Resolve the lookup IDs we'll reference. Use Hidden_Value because it's
-- the stable code we set in the seed migration.
DECLARE @lkpFlagTypeManual int = (
    SELECT TOP 1 Lookup_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = 'FLAG_TYPE' AND Hidden_Value = 'MANUAL'
);
DECLARE @lkpSeverityMedium int = (
    SELECT TOP 1 Lookup_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = 'FLAG_SEVERITY' AND Hidden_Value = 'MEDIUM'
);
DECLARE @lkpCategoryTbml int = (
    SELECT TOP 1 Lookup_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = 'FLAG_CATEGORY' AND Hidden_Value = 'TBML'
);
DECLARE @lkpCategoryKyc int = (
    SELECT TOP 1 Lookup_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = 'FLAG_CATEGORY' AND Hidden_Value = 'KYC'
);

IF @lkpFlagTypeManual IS NULL OR @lkpSeverityMedium IS NULL
   OR @lkpCategoryTbml IS NULL OR @lkpCategoryKyc IS NULL
BEGIN
    RAISERROR ('Required FLAG_* lookup rows not found. Run 2026_05_012_SeedFlagLookups.sql first.', 16, 1);
    RETURN;
END

PRINT CONCAT('Seeding flag catalogue. Manual=', @lkpFlagTypeManual,
             ', Medium=', @lkpSeverityMedium,
             ', TBML=',   @lkpCategoryTbml,
             ', KYC=',    @lkpCategoryKyc);

-- -------------------------------------------------------------------------
-- Step A — Snapshot source rows into a temp table so we can reuse the
-- normalised description in both the catalogue and the scope insert
-- without re-running PATINDEX/LTRIM/RTRIM expressions every time.
--
-- Description is canonical = LTRIM(RTRIM(Field_Label)). Two rows whose
-- labels differ only by leading/trailing whitespace or trailing newlines
-- collapse into a single catalogue entry.
-- -------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#FlagSource') IS NOT NULL DROP TABLE #FlagSource;

SELECT
    Tenant_Field_Setup_Id,
    Product_Id,
    Tab_Id,
    Field_Name,
    Field_Sequence,
    Locale_ID,
    Locale_Label,
    Visibility,
    -- Canonical description (trim leading/trailing whitespace + line breaks)
    LTRIM(RTRIM(REPLACE(REPLACE(Field_Label, CHAR(13), ''), CHAR(10), '')))   AS Flag_Description,
    -- First ~100 chars for the short name (admin can edit later)
    LEFT(LTRIM(RTRIM(REPLACE(REPLACE(Field_Label, CHAR(13), ''), CHAR(10), ''))), 100) AS Flag_Name
INTO #FlagSource
FROM dbo.TmX_Tenant_Field_Setup
WHERE Field_Type_Lkp     = 28
  AND Field_Name         LIKE '%MRL%'
  AND Field_Table_Name   = 'TmxTransactionDetail[]'   -- excludes 114 "Dummy" placeholder rows
  AND Field_Label        IS NOT NULL
  AND LEN(LTRIM(RTRIM(Field_Label))) > 0;

DECLARE @sourceCount int = (SELECT COUNT(*) FROM #FlagSource);
PRINT CONCAT('Step A: ', @sourceCount, ' source MRL rows snapshotted.');

-- -------------------------------------------------------------------------
-- Step B — Catalogue insert.
-- One row per distinct Flag_Description. Category derived from the
-- products that carry the description: if EVERY carrier is Product 5
-- (KYC), it's a KYC flag; otherwise TBML.
--
-- Flag_Code is built from category + a stable hash of the description
-- (SHA1 truncated to 8 hex chars). This gives admins something
-- predictable to reference in integrations without exposing the
-- description verbatim.
-- -------------------------------------------------------------------------
INSERT INTO dbo.TmX_Flag_Catalogue
    (Flag_Code,                 Flag_Name,        Flag_Description,
     Flag_Type_Lkp_ID,          Flag_Category_Lkp_ID, Severity_Lkp_ID,
     Default_Weight,            Requires_Evidence,    Active_Flag,
     Created_By, Created_Date)
SELECT
    -- Flag_Code format: "<TBML|KYC>.MRL.<8 hex>"
    CONCAT(
        CASE WHEN MIN(Product_Id) = 5 AND MAX(Product_Id) = 5 THEN 'KYC' ELSE 'TBML' END,
        '.MRL.',
        UPPER(SUBSTRING(CONVERT(varchar(40), HASHBYTES('SHA1', src.Flag_Description), 2), 1, 8))
    )                          AS Flag_Code,
    MIN(src.Flag_Name)         AS Flag_Name,
    src.Flag_Description       AS Flag_Description,
    @lkpFlagTypeManual         AS Flag_Type_Lkp_ID,
    CASE
        WHEN MIN(Product_Id) = 5 AND MAX(Product_Id) = 5 THEN @lkpCategoryKyc
        ELSE @lkpCategoryTbml
    END                        AS Flag_Category_Lkp_ID,
    @lkpSeverityMedium         AS Severity_Lkp_ID,
    1.00                       AS Default_Weight,
    1                          AS Requires_Evidence,
    1                          AS Active_Flag,
    @who                       AS Created_By,
    @now                       AS Created_Date
FROM #FlagSource src
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.TmX_Flag_Catalogue c
    WHERE c.Flag_Description = src.Flag_Description
)
GROUP BY src.Flag_Description;

DECLARE @catalogueInserted int = @@ROWCOUNT;
PRINT CONCAT('Step B: ', @catalogueInserted, ' new catalogue row(s) inserted.');

-- -------------------------------------------------------------------------
-- Step C — Scope insert.
-- One row per source MRL row, joined to catalogue on Flag_Description.
-- Active_Flag derived from Visibility column.
-- -------------------------------------------------------------------------
INSERT INTO dbo.TmX_Flag_Scope
    (Flag_ID,            Product_ID,      Tab_ID,
     Sort_Order,         Active_Flag,     Legacy_Field_Name,
     Created_By, Created_Date)
SELECT
    c.Flag_ID            AS Flag_ID,
    src.Product_Id       AS Product_ID,
    src.Tab_Id           AS Tab_ID,
    ISNULL(src.Field_Sequence, 0) AS Sort_Order,
    CASE WHEN src.Visibility = '0' THEN 0 ELSE 1 END AS Active_Flag,
    src.Field_Name       AS Legacy_Field_Name,
    @who                 AS Created_By,
    @now                 AS Created_Date
FROM #FlagSource src
INNER JOIN dbo.TmX_Flag_Catalogue c
        ON c.Flag_Description = src.Flag_Description
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.TmX_Flag_Scope s
    WHERE s.Flag_ID            = c.Flag_ID
      AND s.Product_ID         = src.Product_Id
      AND ISNULL(s.Tab_ID, -1) = ISNULL(src.Tab_Id, -1)
      AND ISNULL(s.Legacy_Field_Name, '') = ISNULL(src.Field_Name, '')
);

DECLARE @scopeInserted int = @@ROWCOUNT;
PRINT CONCAT('Step C: ', @scopeInserted, ' new scope row(s) inserted.');

-- -------------------------------------------------------------------------
-- Step D — Confirmation summary
-- -------------------------------------------------------------------------
PRINT '';
PRINT 'Catalogue by category:';
SELECT
    cat.Visible_Value AS Category,
    COUNT(*)          AS Catalogue_Rows
FROM   dbo.TmX_Flag_Catalogue c
LEFT JOIN dbo.TmX_Lookup cat ON cat.Lookup_ID = c.Flag_Category_Lkp_ID
GROUP BY cat.Visible_Value
ORDER BY cat.Visible_Value;

PRINT '';
PRINT 'Scope rows by product (active / inactive):';
SELECT
    s.Product_ID,
    SUM(CASE WHEN s.Active_Flag = 1 THEN 1 ELSE 0 END) AS Active,
    SUM(CASE WHEN s.Active_Flag = 0 THEN 1 ELSE 0 END) AS Inactive,
    COUNT(*)                                           AS Total
FROM   dbo.TmX_Flag_Scope s
GROUP BY s.Product_ID
ORDER BY s.Product_ID;

PRINT '';
PRINT 'Top 5 catalogue rows by scope-row count (most-replicated flags):';
SELECT TOP 5
    c.Flag_ID,
    c.Flag_Code,
    LEFT(c.Flag_Description, 120) + CASE WHEN LEN(c.Flag_Description) > 120 THEN '...' ELSE '' END AS Description_Preview,
    (SELECT COUNT(*) FROM dbo.TmX_Flag_Scope s WHERE s.Flag_ID = c.Flag_ID) AS Scope_Row_Count
FROM   dbo.TmX_Flag_Catalogue c
ORDER  BY (SELECT COUNT(*) FROM dbo.TmX_Flag_Scope s WHERE s.Flag_ID = c.Flag_ID) DESC;

DROP TABLE #FlagSource;

GO

PRINT '';
PRINT 'Slice 8 Step 3 — flag catalogue + scope seed complete.';
PRINT 'Next: 2026_05_014_BackfillTransactionFlagsFromUdf.sql to populate transaction-level flag values.';
