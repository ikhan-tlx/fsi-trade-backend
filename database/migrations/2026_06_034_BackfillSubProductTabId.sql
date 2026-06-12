-- =========================================================================
-- 2026_06_034_BackfillSubProductTabId.sql
--
-- 2026-06-03 — backfill TmX_Transaction.Sub_Product_Tab_Id for transactions
-- created before the column existed (migration 2026_06_033).
--
-- Why: the column decides which tab's fields (and values) the read-only
-- "Transaction details" readout shows. New transactions get it at save time;
-- existing rows need it derived from data so the detail page works for them
-- too.
--
-- How: for each transaction whose Sub_Product_Tab_Id is still NULL, look at
-- its TmX_Transaction_Detail.UDF_Data, find the product's ProductTab selector
-- checkbox whose value is truthy, and read that checkbox's own UDF_Data
-- {"TabId":N} as the tab.
--
--   UDF key shape: "tf{Tenant_Field_Setup_Id}" (legacy rows may use the bare
--   id, or the selector field name, so all are matched).
--
-- Idempotent: only fills rows where Sub_Product_Tab_Id IS NULL — safe to
-- re-run. Transaction-wrapped with a verification count.
--
-- REQUIRES: OPENJSON / ISJSON (SQL Server 2016+, DB compatibility level >= 130).
-- KYC / single-tab products have no ProductTab selector, so they correctly
-- stay NULL (the read side shows every tab).
--
-- Run on: ICBC_DEMO  (AFTER 2026_06_033)
--   sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E -i "<path>\2026_06_034_BackfillSubProductTabId.sql"
-- =========================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY

IF COL_LENGTH('dbo.TmX_Transaction', 'Sub_Product_Tab_Id') IS NULL
    THROW 50001, 'Sub_Product_Tab_Id column is missing — run 2026_06_033 first.', 1;

DECLARE @KycProductId INT;
DECLARE @EligibleCount INT;
DECLARE @UpdatedCount INT;
DECLARE @RemainingCount INT;

SELECT TOP (1) @KycProductId = Product_Id
FROM dbo.TmX_Product
WHERE Product_Name = 'KYC';

IF @KycProductId IS NULL
    THROW 50002, 'KYC product row not found in dbo.TmX_Product.', 1;

SELECT @EligibleCount = COUNT(*)
FROM dbo.TmX_Transaction t
WHERE t.Product_Id <> @KycProductId
  AND t.Sub_Product_Tab_Id IS NULL;

;WITH candidate AS
(
    SELECT
        t.Transaction_Id,
        t.Product_Id,
        d.UDF_Data AS Transaction_UDF_Data,
        f.Tenant_Field_Setup_Id,
        COALESCE(
            TRY_CAST(JSON_VALUE(f.UDF_Data, '$.TabId') AS INT),
            f.Tab_Id
        ) AS Resolved_Tab_Id,
        kv.[key]   AS UdfKey,
        kv.[value] AS UdfValue,
        ROW_NUMBER() OVER (PARTITION BY t.Transaction_Id ORDER BY f.Tenant_Field_Setup_Id) AS rn
    FROM dbo.TmX_Transaction t
    JOIN dbo.TmX_Transaction_Detail d
        ON d.Transaction_Id = t.Transaction_Id
    CROSS APPLY OPENJSON(d.UDF_Data) AS kv
    JOIN dbo.TmX_Tenant_Field_Setup f
        ON  f.Product_Id        = t.Product_Id
        AND f.Field_Lookup_Type = 'ProductTab'
        AND f.Is_Active         = 1
    WHERE t.Sub_Product_Tab_Id IS NULL
      AND t.Product_Id <> @KycProductId
      AND d.UDF_Data IS NOT NULL
      AND ISJSON(d.UDF_Data) = 1
      AND (
            kv.[key] COLLATE DATABASE_DEFAULT = 'tf' + CAST(f.Tenant_Field_Setup_Id AS VARCHAR(20))
         OR kv.[key] COLLATE DATABASE_DEFAULT = CAST(f.Tenant_Field_Setup_Id AS VARCHAR(20))
         OR kv.[key] COLLATE DATABASE_DEFAULT = f.Field_Name
      )
      AND LOWER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), kv.[value]))))
          IN ('true','1','yes','y')
)
, resolved AS
(
    SELECT
        Transaction_Id,
        Resolved_Tab_Id,
        rn
    FROM candidate
    WHERE Resolved_Tab_Id IS NOT NULL
)
UPDATE t
SET    t.Sub_Product_Tab_Id = r.Resolved_Tab_Id
FROM   dbo.TmX_Transaction t
JOIN   resolved r
    ON r.Transaction_Id = t.Transaction_Id
   AND r.rn = 1
WHERE  t.Sub_Product_Tab_Id IS NULL;

SET @UpdatedCount = @@ROWCOUNT;

SELECT @RemainingCount = COUNT(*)
FROM dbo.TmX_Transaction t
WHERE t.Product_Id <> @KycProductId
  AND t.Sub_Product_Tab_Id IS NULL;

PRINT 'Eligible non-KYC rows with NULL Sub_Product_Tab_Id: ' + CAST(@EligibleCount AS VARCHAR(20));
PRINT 'Rows updated by this migration: ' + CAST(@UpdatedCount AS VARCHAR(20));
PRINT 'Remaining unmatched non-KYC rows: ' + CAST(@RemainingCount AS VARCHAR(20));

-- ============================================================
-- VERIFICATION
-- ============================================================

SELECT
    SUM(CASE WHEN Sub_Product_Tab_Id IS NOT NULL THEN 1 ELSE 0 END) AS with_tab,
    SUM(CASE WHEN Sub_Product_Tab_Id IS NULL     THEN 1 ELSE 0 END) AS without_tab,
    COUNT(*) AS total
FROM dbo.TmX_Transaction;

;WITH candidate AS
(
    SELECT
        t.Transaction_Id,
        t.Product_Id,
        d.UDF_Data AS Transaction_UDF_Data,
        f.Tenant_Field_Setup_Id,
        COALESCE(
            TRY_CAST(JSON_VALUE(f.UDF_Data, '$.TabId') AS INT),
            f.Tab_Id
        ) AS Resolved_Tab_Id,
        kv.[key]   AS UdfKey,
        kv.[value] AS UdfValue
    FROM dbo.TmX_Transaction t
    JOIN dbo.TmX_Transaction_Detail d
        ON d.Transaction_Id = t.Transaction_Id
    CROSS APPLY OPENJSON(d.UDF_Data) AS kv
    JOIN dbo.TmX_Tenant_Field_Setup f
        ON  f.Product_Id        = t.Product_Id
        AND f.Field_Lookup_Type = 'ProductTab'
        AND f.Is_Active         = 1
    WHERE t.Sub_Product_Tab_Id IS NULL
      AND t.Product_Id <> @KycProductId
      AND d.UDF_Data IS NOT NULL
      AND ISJSON(d.UDF_Data) = 1
      AND (
            kv.[key] COLLATE DATABASE_DEFAULT = 'tf' + CAST(f.Tenant_Field_Setup_Id AS VARCHAR(20))
         OR kv.[key] COLLATE DATABASE_DEFAULT = CAST(f.Tenant_Field_Setup_Id AS VARCHAR(20))
         OR kv.[key] COLLATE DATABASE_DEFAULT = f.Field_Name
      )
      AND LOWER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), kv.[value]))))
          IN ('true','1','yes','y')
)
SELECT
    COUNT(DISTINCT Transaction_Id) AS candidate_transaction_count,
    COUNT(*) AS candidate_row_count
FROM candidate;

;WITH candidate AS
(
    SELECT
        t.Transaction_Id,
        t.Product_Id,
        d.UDF_Data AS Transaction_UDF_Data,
        f.Tenant_Field_Setup_Id,
        COALESCE(
            TRY_CAST(JSON_VALUE(f.UDF_Data, '$.TabId') AS INT),
            f.Tab_Id
        ) AS Resolved_Tab_Id,
        kv.[key]   AS UdfKey,
        kv.[value] AS UdfValue
    FROM dbo.TmX_Transaction t
    JOIN dbo.TmX_Transaction_Detail d
        ON d.Transaction_Id = t.Transaction_Id
    CROSS APPLY OPENJSON(d.UDF_Data) AS kv
    JOIN dbo.TmX_Tenant_Field_Setup f
        ON  f.Product_Id        = t.Product_Id
        AND f.Field_Lookup_Type = 'ProductTab'
        AND f.Is_Active         = 1
    WHERE t.Sub_Product_Tab_Id IS NULL
      AND t.Product_Id <> @KycProductId
      AND d.UDF_Data IS NOT NULL
      AND ISJSON(d.UDF_Data) = 1
      AND (
            kv.[key] COLLATE DATABASE_DEFAULT = 'tf' + CAST(f.Tenant_Field_Setup_Id AS VARCHAR(20))
         OR kv.[key] COLLATE DATABASE_DEFAULT = CAST(f.Tenant_Field_Setup_Id AS VARCHAR(20))
         OR kv.[key] COLLATE DATABASE_DEFAULT = f.Field_Name
      )
      AND LOWER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), kv.[value]))))
          IN ('true','1','yes','y')
)
SELECT
    c.Transaction_Id,
    c.Product_Id,
    c.Transaction_UDF_Data,
    c.Tenant_Field_Setup_Id,
    c.UdfKey,
    c.UdfValue,
    c.Resolved_Tab_Id
FROM candidate c
ORDER BY c.Transaction_Id, c.Tenant_Field_Setup_Id;

COMMIT TRANSACTION;
PRINT '================================================';
PRINT 'Migration 2026_06_034 completed successfully.';
PRINT '================================================';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'ERROR -- migration rolled back.';
    PRINT 'Message : ' + ERROR_MESSAGE();
    PRINT 'Line    : ' + CAST(ERROR_LINE() AS VARCHAR);
    THROW;
END CATCH;