-- =========================================================================
-- 2026_07_022_SeedEsiOutcomeOperand.sql
--
-- 2026-07-13 — ESI — part 4 of 4.
-- Seeds a HIDDEN operand field ESI_Outcome so the Auto-Flags rule engine can
-- target the composite result with zero runtime scanning (e.g. a flag
-- "ESI_Outcome = High").
--
--  * The VALUE is written by the BE compute-at-submit into
--    TmX_Transaction_Detail.UDF_Data as the literal string "High" / "Adequate"
--    (key = Field_Name = "ESI_Outcome"). See the ESI design doc R5.
--  * The field exists only to appear in the Auto-Flags operand catalogue,
--    which requires an allowed type (Dropdown) + IsActive + Product + Tab.
--    It is hidden on the form (Visibility=false) and disabled.
--  * No Field_Lookup_Type: the outcome is compared as a plain string, so an
--    auto-flag uses a StaticValue RHS ("High") — NOT a lookup-bound RHS
--    (binding would compare a Lookup_ID against the stored "High" string).
--
-- Idempotent. Prereq: 021 (ESI tab). Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @now datetime = GETUTCDATE();
DECLARE @who nvarchar(100) = N'System (ESI_Outcome operand seed)';

DECLARE @kycProductId int = (SELECT TOP 1 Product_Id FROM dbo.TmX_Product WHERE Product_Name = N'KYC');
DECLARE @esiTabId     int = (SELECT TOP 1 Tab_Id FROM dbo.TmX_Tab WHERE Tab_Name = N'Economic Substance');
DECLARE @tenantId     int = (SELECT TOP 1 Tenant_Id FROM dbo.TmX_Tenant_Field_Setup WHERE Tab_Id = @esiTabId);
DECLARE @localeId     int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Tenant_Field_Setup WHERE Tab_Id = @esiTabId);
IF @kycProductId IS NULL OR @esiTabId IS NULL BEGIN PRINT 'KYC/ESI tab not found — run 021 first. Abort.'; RETURN; END
IF @tenantId IS NULL SET @tenantId = 1;
IF @localeId IS NULL SET @localeId = 1;

-- Dropdown type code (25) resolved from the FIELD lookup; fallback 25.
DECLARE @dropdown int = (SELECT TOP 1 TRY_CAST(Hidden_Value AS int)
                         FROM dbo.TmX_Lookup WHERE Lookup_Type = N'FIELD' AND Visible_Value = N'Dropdown');
IF @dropdown IS NULL SET @dropdown = 25;

INSERT INTO dbo.TmX_Tenant_Field_Setup
    (Field_Name, Field_Label, Field_Type_Lkp, Field_Sequence, Parent_Tenant_Field_Setup_Id,
     Product_Id, Tab_Id, Tenant_Id, Is_Active, Is_Mandatory, Is_Disabled, Visibility, UDF_Data,
     Locale_ID, Locale_Label, Created_By, Created_Date)
SELECT N'ESI_Outcome', N'Economic Substance outcome', @dropdown, 60, NULL,
       @kycProductId, @esiTabId, @tenantId, 1, N'false', N'true', N'false',
       N'{"esi":{"role":"outcome"}}', @localeId, N'Economic Substance outcome', @who, @now
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup f
                  WHERE f.Tab_Id = @esiTabId AND f.Field_Name = N'ESI_Outcome');

PRINT CONCAT('ESI_Outcome operand: inserted ', @@ROWCOUNT, ' row(s).');
GO

PRINT 'ESI_Outcome operand seed complete.';
