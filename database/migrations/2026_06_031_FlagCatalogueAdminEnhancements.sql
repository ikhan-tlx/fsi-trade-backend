-- =========================================================================
-- 2026_06_031_FlagCatalogueAdminEnhancements.sql
--
-- Slice 8.1 — schema support for the "Manual red flag configuration" admin
-- page (catalogue CRUD + multi-scope incl. all-products, built-in
-- protection, and soft delete).
--
-- Driven by FE_CHANGES_REQUIRED.md (2026-06-01 "Manual red flag config"
-- entry). Three product decisions baked in here (confirmed by FSI,
-- 2026-06-01):
--   * A flag can carry MANY scopes (product / product+tab / all-products).
--   * "KYC submission" is just another TmX_Product row — no special surface.
--   * Delete is a SOFT delete (retire), not a physical row removal.
--
-- What this migration does
-- ------------------------
--   A. TmX_Flag_Catalogue
--        + Is_Built_In   bit  NOT NULL DEFAULT 0   (Slice-8 seed rows = 1)
--        + Is_Deleted    bit  NOT NULL DEFAULT 0   (soft-delete tombstone)
--        + Deleted_By    nvarchar(100) NULL
--        + Deleted_Date  datetime NULL
--        Backfill Is_Built_In = 1 for the seed rows so the admin UI can block
--        "delete" on shipped flags (relabel + deactivate stay open).
--
--   B. TmX_Flag_Scope — allow an "all products" scope
--        Product_ID  INT NOT NULL  ->  INT NULL
--          (NULL, NULL)  = applies to ALL products       (NEW)
--          (P,    NULL)  = product-level
--          (P,    T)     = product + tab (sub-product)
--        Product_ID is an index key, so the filtered unique indexes are
--        dropped, the column altered, then rebuilt — plus a new global-scope
--        unique index and a CHECK so a Tab can't exist without a Product.
--
-- Idempotent: every ALTER / CREATE / UPDATE is guarded; safe to re-run.
-- Transaction-wrapped with a verification result-set (pattern from
-- 2026_05_025).
--
-- NOTE: the matching C# changes ship ALONGSIDE this migration and are NOT
-- applied by this script:
--   * FlagScope.ProductId -> int?  (+ FlagScopeConfiguration)
--   * IsBuiltIn / IsDeleted / DeletedBy / DeletedDate on FlagCatalogue
--     (+ FlagCatalogueConfiguration) and Is_Deleted filtered out of reads
--   * isBuiltIn + scopes[] on FlagCatalogueListItemDto; scope filter
--     (productId/tabId) on ListFlagCatalogueQuery
--   * AddFlagScopeCommand accepts null productId (= all products)
--   * new DELETE /api/v1/Flag/{id} soft-delete (rejects built-ins)
--
-- Run on: ICBC_DEMO
--   sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E -i "<path>\2026_06_031_FlagCatalogueAdminEnhancements.sql"
-- =========================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY

-- ============================================================
-- A1. TmX_Flag_Catalogue — add built-in + soft-delete columns
-- ============================================================
IF COL_LENGTH('dbo.TmX_Flag_Catalogue', 'Is_Built_In') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Flag_Catalogue
        ADD Is_Built_In BIT NOT NULL
            CONSTRAINT DF_FlagCatalogue_IsBuiltIn DEFAULT (0);
    PRINT 'A1 : Is_Built_In added.';
END
ELSE PRINT 'A1 : Is_Built_In already exists -- skipped.';

IF COL_LENGTH('dbo.TmX_Flag_Catalogue', 'Is_Deleted') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Flag_Catalogue
        ADD Is_Deleted BIT NOT NULL
            CONSTRAINT DF_FlagCatalogue_IsDeleted DEFAULT (0);
    PRINT 'A1 : Is_Deleted added.';
END
ELSE PRINT 'A1 : Is_Deleted already exists -- skipped.';

IF COL_LENGTH('dbo.TmX_Flag_Catalogue', 'Deleted_By') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Deleted_By NVARCHAR(100) NULL;
    PRINT 'A1 : Deleted_By added.';
END
ELSE PRINT 'A1 : Deleted_By already exists -- skipped.';

IF COL_LENGTH('dbo.TmX_Flag_Catalogue', 'Deleted_Date') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Deleted_Date DATETIME NULL;
    PRINT 'A1 : Deleted_Date added.';
END
ELSE PRINT 'A1 : Deleted_Date already exists -- skipped.';

-- ============================================================
-- A2. Backfill Is_Built_In = 1 for the Slice-8 seed rows.
--     sp_executesql: Is_Built_In was added above in this same batch, so a
--     direct reference would fail at parse time. Scoped to the seed
--     Created_By so admin-created flags (which carry a real user) are never
--     mis-marked -- keeps the backfill re-run safe.
-- ============================================================
EXEC sp_executesql N'
UPDATE dbo.TmX_Flag_Catalogue
SET Is_Built_In = 1
WHERE Created_By = N''System (Slice 8 catalogue seed)''
  AND Is_Built_In = 0;
';
PRINT 'A2 : Built-in backfill -- rows: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ============================================================
-- B1. Drop the indexes that key on Product_ID. Required before the column
--     can be altered to nullable.
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UQ_TmX_Flag_Scope_FlagProductTab'
             AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    DROP INDEX [UQ_TmX_Flag_Scope_FlagProductTab] ON dbo.TmX_Flag_Scope;

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UQ_TmX_Flag_Scope_FlagProduct_NoTab'
             AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    DROP INDEX [UQ_TmX_Flag_Scope_FlagProduct_NoTab] ON dbo.TmX_Flag_Scope;

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'IX_TmX_Flag_Scope_ProductTab'
             AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    DROP INDEX [IX_TmX_Flag_Scope_ProductTab] ON dbo.TmX_Flag_Scope;

PRINT 'B1 : Product_ID-keyed indexes dropped (if present).';

-- ============================================================
-- B2. Make Product_ID nullable  (NULL = "all products" scope).
-- ============================================================
IF COLUMNPROPERTY(OBJECT_ID('dbo.TmX_Flag_Scope'), 'Product_ID', 'AllowsNull') = 0
BEGIN
    ALTER TABLE dbo.TmX_Flag_Scope ALTER COLUMN Product_ID INT NULL;
    PRINT 'B2 : Product_ID is now nullable.';
END
ELSE PRINT 'B2 : Product_ID already nullable -- skipped.';

-- ============================================================
-- B3. Recreate the scope indexes for the 3-tier model.
--   * product+tab   : unique per (Flag, Product, Tab)            Tab NOT NULL
--   * product-level : unique per (Flag, Product)                 Tab NULL + Product NOT NULL
--   * all-products  : unique per (Flag)                          Product NULL + Tab NULL   (NEW)
-- SQL Server treats NULLs as DISTINCT in a unique index, so the global case
-- needs its own filtered index to stop duplicate (Flag, NULL, NULL) rows;
-- the product-level filter is tightened with "Product_ID IS NOT NULL" so the
-- two don't overlap.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_TmX_Flag_Scope_FlagProductTab'
                 AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    CREATE UNIQUE INDEX [UQ_TmX_Flag_Scope_FlagProductTab]
        ON dbo.TmX_Flag_Scope ([Flag_ID], [Product_ID], [Tab_ID])
        WHERE [Tab_ID] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_TmX_Flag_Scope_FlagProduct_NoTab'
                 AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    CREATE UNIQUE INDEX [UQ_TmX_Flag_Scope_FlagProduct_NoTab]
        ON dbo.TmX_Flag_Scope ([Flag_ID], [Product_ID])
        WHERE [Tab_ID] IS NULL AND [Product_ID] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_TmX_Flag_Scope_FlagAllProducts'
                 AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    CREATE UNIQUE INDEX [UQ_TmX_Flag_Scope_FlagAllProducts]
        ON dbo.TmX_Flag_Scope ([Flag_ID])
        WHERE [Product_ID] IS NULL AND [Tab_ID] IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_TmX_Flag_Scope_ProductTab'
                 AND object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
    CREATE INDEX [IX_TmX_Flag_Scope_ProductTab]
        ON dbo.TmX_Flag_Scope ([Product_ID], [Tab_ID])
        INCLUDE ([Flag_ID], [Sort_Order])
        WHERE [Active_Flag] = 1;

PRINT 'B3 : Scope indexes rebuilt (incl. all-products unique index).';

-- ============================================================
-- B4. Guard: a Tab cannot be scoped without a Product.
--     Valid shapes: (NULL,NULL) (P,NULL) (P,T). Forbids (NULL, T).
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_TmX_Flag_Scope_TabRequiresProduct'
                 AND parent_object_id = OBJECT_ID('dbo.TmX_Flag_Scope'))
BEGIN
    ALTER TABLE dbo.TmX_Flag_Scope WITH CHECK
        ADD CONSTRAINT CK_TmX_Flag_Scope_TabRequiresProduct
        CHECK ([Tab_ID] IS NULL OR [Product_ID] IS NOT NULL);
    PRINT 'B4 : CHECK constraint added (Tab requires Product).';
END
ELSE PRINT 'B4 : CHECK constraint already exists -- skipped.';

-- ============================================================
-- VERIFICATION
--   sp_executesql: Is_Built_In / Is_Deleted referenced below were added in
--   this same batch.
-- ============================================================
EXEC sp_executesql N'
SELECT step_name, result FROM (
    SELECT 1 AS ord, ''Is_Built_In column exists'' AS step_name,
        CASE WHEN COL_LENGTH(''dbo.TmX_Flag_Catalogue'',''Is_Built_In'') IS NOT NULL
             THEN ''PASS'' ELSE ''FAIL'' END AS result
    UNION ALL SELECT 2, ''Is_Deleted column exists'',
        CASE WHEN COL_LENGTH(''dbo.TmX_Flag_Catalogue'',''Is_Deleted'') IS NOT NULL
             THEN ''PASS'' ELSE ''FAIL'' END
    UNION ALL SELECT 3, ''Deleted_By / Deleted_Date exist'',
        CASE WHEN COL_LENGTH(''dbo.TmX_Flag_Catalogue'',''Deleted_By'')   IS NOT NULL
              AND COL_LENGTH(''dbo.TmX_Flag_Catalogue'',''Deleted_Date'') IS NOT NULL
             THEN ''PASS'' ELSE ''FAIL'' END
    UNION ALL SELECT 4, ''Seed flags marked built-in'',
        CASE WHEN NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Flag_Catalogue
            WHERE Created_By = N''System (Slice 8 catalogue seed)'' AND Is_Built_In = 0)
        THEN ''PASS'' ELSE ''FAIL'' END
    UNION ALL SELECT 5, ''Product_ID is nullable'',
        CASE WHEN COLUMNPROPERTY(OBJECT_ID(''dbo.TmX_Flag_Scope''),''Product_ID'',''AllowsNull'') = 1
             THEN ''PASS'' ELSE ''FAIL'' END
    UNION ALL SELECT 6, ''All-products unique index present'',
        CASE WHEN EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = ''UQ_TmX_Flag_Scope_FlagAllProducts''
              AND object_id = OBJECT_ID(''dbo.TmX_Flag_Scope''))
        THEN ''PASS'' ELSE ''FAIL'' END
    UNION ALL SELECT 7, ''Tab-requires-Product CHECK present'',
        CASE WHEN EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE name = ''CK_TmX_Flag_Scope_TabRequiresProduct''
              AND parent_object_id = OBJECT_ID(''dbo.TmX_Flag_Scope''))
        THEN ''PASS'' ELSE ''FAIL'' END
) AS v(ord, step_name, result)
ORDER BY ord;
';

COMMIT TRANSACTION;
PRINT '================================================';
PRINT 'Migration 2026_06_031 completed successfully.';
PRINT '================================================';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'ERROR -- migration rolled back.';
    PRINT 'Message : ' + ERROR_MESSAGE();
    PRINT 'Line    : ' + CAST(ERROR_LINE() AS VARCHAR);
    THROW;
END CATCH;
