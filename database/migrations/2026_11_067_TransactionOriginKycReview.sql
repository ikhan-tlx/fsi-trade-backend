-- =========================================================================
-- 2026_11_067_TransactionOriginKycReview.sql
--
-- 2026-11-13 — Transaction origin for the KYC-review flow.
--   (1) Adds TmX_Transaction.Transaction_Origin_Lkp (INT NULL, FK -> TmX_Lookup)
--       so a transaction can record how it was initiated. NULL = Normal.
--   (2) Seeds a new lookup type TRANSACTION_ORIGIN:
--         Lookup_Type         Visible_Value   Hidden_Value   Sort_Order
--         TRANSACTION_ORIGIN  Normal          NORMAL         10
--         TRANSACTION_ORIGIN  KYC Review      KYC_REVIEW     20
--       The orchestrator stamps the new review KYC with the KYC_REVIEW id so the
--       grid/detail can badge "Initiated by KYC review".
--
-- Idempotent: column guarded by COL_LENGTH; lookups guarded by NOT EXISTS on
-- (Lookup_Type, Hidden_Value). Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

-- (1) Column ---------------------------------------------------------------
IF COL_LENGTH('dbo.TmX_Transaction', 'Transaction_Origin_Lkp') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Transaction ADD Transaction_Origin_Lkp INT NULL;
    PRINT 'Added TmX_Transaction.Transaction_Origin_Lkp.';
END
ELSE
    PRINT 'TmX_Transaction.Transaction_Origin_Lkp already exists.';
GO

-- (2) Lookup type ----------------------------------------------------------
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @localeId int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @localeId IS NULL SET @localeId = 1;

DECLARE @now    datetime      = GETUTCDATE();
DECLARE @nowStr nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who    nvarchar(100) = N'System (TRANSACTION_ORIGIN lookup seed)';

DECLARE @seed TABLE (Visible_Value nvarchar(999), Hidden_Value nvarchar(100), Sort_Order int);
INSERT INTO @seed VALUES
    (N'Normal',     N'NORMAL',     10),
    (N'KYC Review', N'KYC_REVIEW', 20);

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID, Lookup_Type,          Visible_Value, Hidden_Value, Description,
     Is_Active, Active_Flag, User_Editable, Sort_Order, Lookup_Name,
     Locale_ID, Locale_Label, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT
     @tenantId, N'TRANSACTION_ORIGIN', s.Visible_Value, s.Hidden_Value, N'How the transaction was initiated.',
     1,         1,           0,             s.Sort_Order, s.Visible_Value,
     @localeId, s.Visible_Value, @who,      @nowStr,      @who,           @now
FROM @seed s
WHERE NOT EXISTS (
     SELECT 1 FROM dbo.TmX_Lookup l
     WHERE l.Lookup_Type = N'TRANSACTION_ORIGIN' AND l.Hidden_Value = s.Hidden_Value);

PRINT CONCAT('TRANSACTION_ORIGIN lookup: inserted ', @@ROWCOUNT, ' row(s).');

SELECT Lookup_ID, Lookup_Type, Visible_Value, Hidden_Value, Sort_Order
FROM   dbo.TmX_Lookup WHERE Lookup_Type = N'TRANSACTION_ORIGIN' ORDER BY Sort_Order;
GO
