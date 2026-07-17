/* 2026_07_014_RetypeAndMarkAmountFields.sql
   ---------------------------------------------------------------------------
   Prerequisite data fix for the Customer Intelligence trade-amount rollups.

   Step 1  Re-type mis-labelled AMOUNT fields:
           fields that are TEXT (Field_Type_Lkp = 3) whose Field_Label contains
           "amount" are actually amount fields -> set Field_Type_Lkp = 64 (Amount).

   Step 2  Mark THE amount field per sub-product (Is_Amount_Field):
           for each non-KYC product / sub-product tab, the amount field
           (Field_Type_Lkp = 64, Is_Active = 1) with the MINIMUM Field_Sequence
           is flagged Is_Amount_Field = 1; all other amount fields in the same
           sub-product are cleared to 0 (mutually exclusive per sub-product).
           KYC products are left untouched (declaration only, no amount marking).

   Idempotent + atomic: safe to re-run (Step 2 clears then re-marks).
   ------------------------------------------------------------------------- */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @c int;

    /* ---- Step 1: TEXT(3) + label contains "amount"  ->  AMOUNT(64) ---- */
    UPDATE fs
       SET Field_Type_Lkp = 64
    FROM dbo.TmX_Tenant_Field_Setup AS fs
    WHERE fs.Field_Type_Lkp = 3
      AND LOWER(fs.Field_Label) LIKE '%amount%';
    SET @c = @@ROWCOUNT;
    PRINT 'Step 1: re-typed ' + CAST(@c AS varchar(10)) + ' TEXT field(s) to AMOUNT(64).';

    /* ---- Step 2a: clear Is_Amount_Field on all non-KYC fields (re-marked below) ---- */
    UPDATE fs
       SET Is_Amount_Field = 0
    FROM dbo.TmX_Tenant_Field_Setup AS fs
    JOIN dbo.TmX_Product AS p ON p.Product_ID = fs.Product_Id
    WHERE p.Product_Name NOT LIKE '%KYC%'
      AND fs.Is_Amount_Field = 1;
    SET @c = @@ROWCOUNT;
    PRINT 'Step 2a: cleared Is_Amount_Field on ' + CAST(@c AS varchar(10)) + ' non-KYC field(s).';

    /* ---- Step 2b: mark the min-sequence AMOUNT(64) field per non-KYC sub-product ---- */
    ;WITH targets AS (
        SELECT fs.Tenant_Field_Setup_Id,
               ROW_NUMBER() OVER (PARTITION BY fs.Product_Id, fs.Tab_Id
                                  ORDER BY fs.Field_Sequence ASC, fs.Tenant_Field_Setup_Id ASC) AS rn
        FROM dbo.TmX_Tenant_Field_Setup AS fs
        JOIN dbo.TmX_Product AS p ON p.Product_ID = fs.Product_Id
        WHERE fs.Field_Type_Lkp = 64
          AND fs.Is_Active      = 1
          AND LOWER(fs.Field_Label) LIKE '%amount%'   -- only real amount fields (skip mis-typed Product Code / Version / Dummy)
          AND p.Product_Name NOT LIKE '%KYC%'
    )
    UPDATE fs
       SET Is_Amount_Field = 1
    FROM dbo.TmX_Tenant_Field_Setup AS fs
    JOIN targets AS tg ON tg.Tenant_Field_Setup_Id = fs.Tenant_Field_Setup_Id
    WHERE tg.rn = 1;
    SET @c = @@ROWCOUNT;
    PRINT 'Step 2b: marked Is_Amount_Field on ' + CAST(@c AS varchar(10)) + ' field(s) (one per non-KYC sub-product).';

    COMMIT TRANSACTION;
    PRINT 'Migration 2026_07_014 committed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Migration 2026_07_014 FAILED: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
