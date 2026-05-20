-- =========================================================================
-- 2026_05_009_SeedApplicationCancelledLookup.sql
--
-- Slice 6 Step 5 — seeds the missing "Application Cancelled" lookup row
-- under Lookup_Type = 'APPLICATION_STATUS'. The cancel-transaction
-- handler (CancelTransactionCommandHandler) resolves this row to find the
-- Lookup_ID it writes into Transaction_Status_Lkp.
--
-- Without this row the handler logs a warning ("No active TmX_Lookup row
-- for Lookup_Type='APPLICATION_STATUS' Visible_Value='Application Cancelled'")
-- and leaves Transaction_Status_Lkp unchanged. The workflow setState to
-- the cancelled terminal still runs — but the row-level status doesn't
-- reflect the cancellation, so the FE grid + filters keep showing the old
-- status. This seed closes that gap.
--
-- Idempotent — safe to re-run. Guarded by NOT EXISTS on
-- (Lookup_Type, Visible_Value).
--
-- Tenant_ID / Locale_ID are derived from any existing APPLICATION_STATUS
-- row so the new row sits naturally alongside its siblings. Falls back to
-- (Tenant_ID = 1, Locale_ID = 1) if the APPLICATION_STATUS type is empty
-- (unlikely — the legacy seed already populates other statuses).
--
-- Hidden_Value matches Visible_Value to keep the row consistent with the
-- legacy mapping convention (Hidden_Value is the machine-readable code,
-- Visible_Value is the display label; for status names both are "Application
-- Cancelled" since the FE uses the visible value directly).
--
-- Run on:  ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @lookupType    nvarchar(100) = N'APPLICATION_STATUS';
DECLARE @visibleValue  nvarchar(999) = N'Application Cancelled';
DECLARE @hiddenValue   nvarchar(100) = N'Application Cancelled';
DECLARE @description   nvarchar(500) = N'Transaction has been cancelled. Terminal status — workflow is at the cancelled state.';

-- Derive Tenant_ID + Locale_ID from a sibling APPLICATION_STATUS row so
-- this new row is consistent with the existing roster. If APPLICATION_STATUS
-- is empty (shouldn't be on a real deployment), fall back to (1, 1).
DECLARE @tenantId int = (
    SELECT TOP 1 Tenant_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = @lookupType
    ORDER  BY Lookup_ID
);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @localeId int = (
    SELECT TOP 1 Locale_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = @lookupType
    ORDER  BY Lookup_ID
);
IF @localeId IS NULL SET @localeId = 1;

-- Next sort-order value sits AFTER all existing APPLICATION_STATUS rows
-- so the new entry shows up at the end of any "all statuses" dropdown.
DECLARE @sortOrder int = (
    SELECT ISNULL(MAX(Sort_Order), 0) + 10
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = @lookupType
);

DECLARE @now            datetime      = GETUTCDATE();
DECLARE @nowAsNvarchar  nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who            nvarchar(100) = N'System (Slice 6 Step 5 seed)';

PRINT CONCAT('Seeding APPLICATION_STATUS lookup with Tenant_ID=', @tenantId,
             ', Locale_ID=', @localeId,
             ', Sort_Order=', @sortOrder);

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID,         Lookup_Type,      Visible_Value,   Hidden_Value,   Description,
     Is_Active,         Active_Flag,      User_Editable,   Sort_Order,
     Lookup_Name,
     Locale_ID,         Locale_Label,
     Created_By,        Created_Date,
     Last_Updated_By,   Last_Updated_Date)
SELECT
    @tenantId,          @lookupType,      @visibleValue,   @hiddenValue,   @description,
    1,                  1,                0,               @sortOrder,
    @visibleValue,
    @localeId,          @visibleValue,
    @who,               @nowAsNvarchar,
    @who,               @now
WHERE NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type    = @lookupType
      AND  Visible_Value  = @visibleValue
);

DECLARE @inserted int = @@ROWCOUNT;
PRINT CONCAT('Inserted ', @inserted, ' new APPLICATION_STATUS row(s).');

PRINT '';
PRINT 'Current APPLICATION_STATUS roster (post-seed):';
SELECT  Lookup_ID,
        Visible_Value,
        Hidden_Value,
        Is_Active,
        Active_Flag,
        Sort_Order
FROM    dbo.TmX_Lookup
WHERE   Lookup_Type = @lookupType
ORDER   BY Sort_Order, Lookup_ID;

GO

PRINT 'Slice 6 Step 5 APPLICATION_STATUS seed complete.';
