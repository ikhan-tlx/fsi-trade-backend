/* 2026_07_017_DropTransactionCurrencyId.sql  (Phase 2 of 2)
   ---------------------------------------------------------------------------
   Drops TmX_Transaction.Currency_Id and its FK to TmX_Currency, now that the
   app reads/writes Currency_Lkp (FK -> TmX_Lookup) instead (2026_07_016).

   Run ONLY after the Currency_Lkp code is deployed (it is). Idempotent.
   The UDF still stores currency + amount exactly as before.
   ------------------------------------------------------------------------- */

/* drop the FK to TmX_Currency (name from the live constraint) */
IF EXISTS (SELECT 1 FROM sys.foreign_keys
           WHERE name = 'Fk_Trans_To_Currency'
             AND parent_object_id = OBJECT_ID('dbo.TmX_Transaction'))
    ALTER TABLE dbo.TmX_Transaction DROP CONSTRAINT Fk_Trans_To_Currency;
GO

/* defensively drop any other FK still referencing the Currency_Id column */
DECLARE @fk sysname;
SELECT TOP 1 @fk = fk.name
FROM sys.foreign_key_columns fkc
JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
WHERE fkc.parent_object_id = OBJECT_ID('dbo.TmX_Transaction')
  AND fkc.parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.TmX_Transaction'), 'Currency_Id', 'ColumnId');
IF @fk IS NOT NULL
    EXEC('ALTER TABLE dbo.TmX_Transaction DROP CONSTRAINT ' + @fk);
GO

/* drop the column */
IF COL_LENGTH('dbo.TmX_Transaction', 'Currency_Id') IS NOT NULL
    ALTER TABLE dbo.TmX_Transaction DROP COLUMN Currency_Id;
GO

PRINT 'Currency_Id dropped; currency now stored in Currency_Lkp (FK -> TmX_Lookup).';
