/* 2026_07_016_TransactionCurrencyLkpColumn.sql  (Phase 1 of 2)
   ---------------------------------------------------------------------------
   Adds TmX_Transaction.Currency_Lkp — the currency as a TmX_Lookup id (the
   representation stored in the transaction UDF Currency field), replacing the
   old Currency_Id (which FK'd to TmX_Currency and did not match the lookup-id
   currency values). Currency is sourced from the UDF, matching the app change.

   This phase is ADDITIVE and safe: Currency_Id is left in place. Phase 2
   (2026_07_017) drops Currency_Id after the new code is deployed + verified.

   Backfill: from the sub-product's Currency dropdown (Field_Type_Lkp = 25,
   Field_Lookup_Type = 'Currency'), non-KYC only, and ONLY when the value is a
   valid TmX_Lookup id (guard) so the FK below validates cleanly.
   The UDF continues to store currency + amount exactly as before.
   ------------------------------------------------------------------------- */

/* ---- batch 1: add column ---- */
IF COL_LENGTH('dbo.TmX_Transaction', 'Currency_Lkp') IS NULL
    ALTER TABLE dbo.TmX_Transaction ADD Currency_Lkp INT NULL;
GO

/* ---- batch 2: backfill from UDF (non-KYC, valid lookup only) ---- */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @c int;

UPDATE t
   SET t.Currency_Lkp = x.Cur
FROM dbo.TmX_Transaction AS t
JOIN dbo.TmX_Product     AS p ON p.Product_ID = t.Product_Id AND p.Product_Name NOT LIKE '%KYC%'
CROSS APPLY (
    SELECT TOP 1 TRY_CONVERT(int, j.[value]) AS Cur
    FROM dbo.TmX_Transaction_Detail AS td
    JOIN dbo.TmX_Tenant_Field_Setup AS cf
           ON cf.Field_Type_Lkp    = 25
          AND cf.Field_Lookup_Type = 'Currency'
          AND cf.Product_Id = t.Product_Id
          AND ((cf.Tab_Id = t.Sub_Product_Tab_Id) OR (cf.Tab_Id IS NULL AND t.Sub_Product_Tab_Id IS NULL))
    CROSS APPLY OPENJSON(td.UDF_Data) AS j
    WHERE td.Transaction_Id = t.Transaction_Id
      AND td.UDF_Data IS NOT NULL AND ISJSON(td.UDF_Data) = 1
      AND j.[key] COLLATE DATABASE_DEFAULT = cf.Field_Name
      AND TRY_CONVERT(int, j.[value]) IS NOT NULL
      AND EXISTS (SELECT 1 FROM dbo.TmX_Lookup l WHERE l.Lookup_ID = TRY_CONVERT(int, j.[value]))
) AS x
WHERE t.Currency_Lkp IS NULL AND x.Cur IS NOT NULL;
SET @c = @@ROWCOUNT;
PRINT 'Backfilled Currency_Lkp on ' + CAST(@c AS varchar(10)) + ' non-KYC transaction(s).';
GO

/* ---- batch 3: FK to TmX_Lookup (values were validated above) ---- */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TmX_Transaction_Currency_Lkp')
    ALTER TABLE dbo.TmX_Transaction WITH CHECK
        ADD CONSTRAINT FK_TmX_Transaction_Currency_Lkp
        FOREIGN KEY (Currency_Lkp) REFERENCES dbo.TmX_Lookup (Lookup_ID);
GO
