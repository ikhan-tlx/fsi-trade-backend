/* ============================================================================
   2026_07_004_AmountFieldOnFieldSetup.sql

   Moves the "auto-resolved amount field" designation onto the field config,
   per sub-product (Tab), replacing the per-product map table.

   1. TmX_Tenant_Field_Setup.Is_Amount_Field (bit, NOT NULL, DEFAULT 0)
        Marks the single field, per (Product, Tab) sub-product, whose value is
        the transaction amount — used by cross-transaction Sum/Avg/Min/Max
        aggregates. Managed on the Field Configuration page as a per-sub-product
        radio (mutual exclusivity enforced by the write handler across the
        field's locale rows).

   2. Drops dbo.TmX_Product_Aggregate_Field_Map (introduced in 2026_07_002,
        empty/unused) — its role is now the field-config flag above.

   Additive + idempotent. Safe to re-run.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_Tenant_Field_Setup')
                 AND name = 'Is_Amount_Field')
BEGIN
    ALTER TABLE dbo.TmX_Tenant_Field_Setup
        ADD [Is_Amount_Field] bit NOT NULL
            CONSTRAINT [DF_TenantFieldSetup_IsAmountField] DEFAULT (0);
    PRINT 'Added dbo.TmX_Tenant_Field_Setup.Is_Amount_Field (default 0).';
END
ELSE
    PRINT 'dbo.TmX_Tenant_Field_Setup.Is_Amount_Field already exists — skipped.';
GO

/* Index to resolve the amount field per (Product, Tab) quickly at evaluation. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_TenantFieldSetup_AmountField'
                 AND object_id = OBJECT_ID('dbo.TmX_Tenant_Field_Setup'))
BEGIN
    CREATE INDEX [IX_TenantFieldSetup_AmountField]
        ON dbo.TmX_Tenant_Field_Setup ([Product_Id], [Tab_Id])
        INCLUDE ([Field_Name], [Is_Amount_Field]);
    PRINT 'Created index IX_TenantFieldSetup_AmountField.';
END
ELSE
    PRINT 'IX_TenantFieldSetup_AmountField already exists — skipped.';
GO

/* Drop the now-redundant per-product map table (2026_07_002). */
IF OBJECT_ID('dbo.TmX_Product_Aggregate_Field_Map', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.TmX_Product_Aggregate_Field_Map;
    PRINT 'Dropped dbo.TmX_Product_Aggregate_Field_Map (superseded by field-config flag).';
END
ELSE
    PRINT 'dbo.TmX_Product_Aggregate_Field_Map not present — skipped.';
GO

PRINT '2026_07_004_AmountFieldOnFieldSetup.sql complete.';
GO
