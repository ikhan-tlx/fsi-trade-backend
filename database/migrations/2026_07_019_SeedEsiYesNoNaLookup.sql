-- =========================================================================
-- 2026_07_019_SeedEsiYesNoNaLookup.sql
--
-- 2026-07-13 — ESI (Economic Substance Indicator) — part 1 of 4.
-- Seeds a NEW lookup type ESI_YES_NO_NA (Yes / No / N-A) used by the ESI
-- questionnaire checkpoints. The existing "Yes or No" lookup is left
-- untouched. Stored answer = the option's Lookup_ID; the ESI compute
-- resolves it to Hidden_Value (Y / N / NA) to test high-risk.
--
--   Lookup_Type     Visible_Value   Hidden_Value   Sort_Order
--   -----------     -------------   ------------   ----------
--   ESI_YES_NO_NA   Yes             Y              10
--   ESI_YES_NO_NA   No              N              20
--   ESI_YES_NO_NA   N/A             NA             30
--
-- Idempotent: guarded by NOT EXISTS on (Lookup_Type, Hidden_Value).
-- Tenant_ID / Locale_ID derived from any sibling lookup row.
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @localeId int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @localeId IS NULL SET @localeId = 1;

DECLARE @now           datetime      = GETUTCDATE();
DECLARE @nowStr        nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who           nvarchar(100) = N'System (ESI_YES_NO_NA lookup seed)';

DECLARE @seed TABLE (Visible_Value nvarchar(999), Hidden_Value nvarchar(100), Sort_Order int);
INSERT INTO @seed VALUES
    (N'Yes', N'Y',  10),
    (N'No',  N'N',  20),
    (N'N/A', N'NA', 30);

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID, Lookup_Type,        Visible_Value, Hidden_Value, Description,
     Is_Active, Active_Flag, User_Editable, Sort_Order, Lookup_Name,
     Locale_ID, Locale_Label, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT
     @tenantId, N'ESI_YES_NO_NA',   s.Visible_Value, s.Hidden_Value, N'ESI checkpoint answer.',
     1,         1,           0,             s.Sort_Order, s.Visible_Value,
     @localeId, s.Visible_Value, @who,      @nowStr,      @who,           @now
FROM @seed s
WHERE NOT EXISTS (
     SELECT 1 FROM dbo.TmX_Lookup l
     WHERE l.Lookup_Type = N'ESI_YES_NO_NA' AND l.Hidden_Value = s.Hidden_Value);

PRINT CONCAT('ESI_YES_NO_NA lookup: inserted ', @@ROWCOUNT, ' row(s).');

SELECT Lookup_ID, Lookup_Type, Visible_Value, Hidden_Value, Sort_Order, Is_Active
FROM   dbo.TmX_Lookup WHERE Lookup_Type = N'ESI_YES_NO_NA' ORDER BY Sort_Order;
GO

PRINT 'ESI_YES_NO_NA lookup seed complete.';
