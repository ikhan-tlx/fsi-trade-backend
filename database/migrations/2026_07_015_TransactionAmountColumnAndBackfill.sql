/* 2026_07_015_TransactionAmountColumnAndBackfill.sql
   ---------------------------------------------------------------------------
   Adds a typed Transaction_Amount column to TmX_Transaction and backfills it
   (and Currency_Id) for existing NON-KYC transactions from the transaction UDF,
   so the Customer Intelligence trade-amount rollups read a real column instead
   of parsing JSON.

   Amount source : the sub-product's marked Is_Amount_Field field value (flat at UDF root).
   Currency source: the sub-product's Currency dropdown (Field_Type_Lkp = 25,
                    Field_Lookup_Type = 'Currency') — only where Currency_Id is null.
   Scope         : NON-KYC products only (Product_Name NOT LIKE '%KYC%'), matching the
                   Is_Amount_Field marking in 2026_07_014.

   Additive + idempotent. Existing save behaviour is unchanged (UDF still written);
   the app's save flow additionally populates these columns going forward.
   A correlated TOP-1 sub-query is used so any per-locale field rows collapse to a
   single value per transaction.
   ------------------------------------------------------------------------- */

/* ---- batch 1: add the column ---- */
IF COL_LENGTH('dbo.TmX_Transaction', 'Transaction_Amount') IS NULL
    ALTER TABLE dbo.TmX_Transaction ADD Transaction_Amount decimal(18,2) NULL;
GO

/* ---- batch 2: backfill from UDF (non-KYC only) ---- */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @c int;

/* Amount */
UPDATE t
   SET t.Transaction_Amount = x.Amt
FROM dbo.TmX_Transaction AS t
JOIN dbo.TmX_Product     AS p ON p.Product_ID = t.Product_Id AND p.Product_Name NOT LIKE '%KYC%'
CROSS APPLY (
    SELECT TOP 1 TRY_CONVERT(decimal(18,2), j.[value]) AS Amt
    FROM dbo.TmX_Transaction_Detail AS td
    JOIN dbo.TmX_Tenant_Field_Setup AS af
           ON af.Is_Amount_Field = 1
          AND af.Product_Id = t.Product_Id
          AND ((af.Tab_Id = t.Sub_Product_Tab_Id) OR (af.Tab_Id IS NULL AND t.Sub_Product_Tab_Id IS NULL))
    CROSS APPLY OPENJSON(td.UDF_Data) AS j
    WHERE td.Transaction_Id = t.Transaction_Id
      AND td.UDF_Data IS NOT NULL AND ISJSON(td.UDF_Data) = 1
      AND j.[key] COLLATE DATABASE_DEFAULT = af.Field_Name
      AND TRY_CONVERT(decimal(18,2), j.[value]) IS NOT NULL
) AS x
WHERE t.Transaction_Amount IS NULL AND x.Amt IS NOT NULL;
SET @c = @@ROWCOUNT;
PRINT 'Backfilled Transaction_Amount on ' + CAST(@c AS varchar(10)) + ' non-KYC transaction(s).';

/* Currency_Id is intentionally NOT backfilled from the UDF.
   TmX_Transaction.Currency_Id is already populated from the transaction header
   (the currency dropdown) via the save handlers, and it is FK-constrained to
   TmX_Currency.Currency_ID. The UDF Currency field value is a different
   representation and does NOT equal that PK (it FK-violates), so deriving it
   from the UDF would corrupt data / break the statement. Currency therefore
   stays sourced from the header, unchanged. */
PRINT 'Currency_Id left header-sourced (not backfilled from UDF; see note above).';
GO
