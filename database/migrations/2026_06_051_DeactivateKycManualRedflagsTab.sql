-- =========================================================================
-- 2026_06_051_DeactivateKycManualRedflagsTab.sql
--
-- 2026-06-09 — hide the KYC "Manual Redflags" tab from the transaction
-- wizard / form. Manual flags have migrated to the new structure: they are
-- surfaced through the dedicated "Manual Flags" header control (the flag
-- pill), NOT as a form tab.
--
-- The form definition (GetProductFormDefinitionQueryHandler) builds its tab
-- list from dbo.TmX_Entity_Tab_Product_Mapping (rows with Is_Active = 1),
-- then joins dbo.TmX_Tab for names. So the EFFECTIVE removal is to set the
-- MAPPING row's Is_Active = 0 — deactivating only the TmX_Tab row merely
-- nulls the tab's name while the tab still appears (its mapping is still
-- active). We do both for consistency.
--
-- Target tab: TmX_Tab.Hidden_Value = 'KYC Manual Redflags' (Tab_Id 34).
-- Keyed on Hidden_Value (stable across environments). Idempotent — only
-- flips rows currently Is_Active/Active_Flag = 1. Safe to re-run.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @who  nvarchar(100) = N'System (deactivate KYC Manual Redflags tab)';
DECLARE @now  datetime      = GETUTCDATE();

DECLARE @tabId int =
    (SELECT TOP 1 Tab_Id FROM dbo.TmX_Tab WHERE Hidden_Value = N'KYC Manual Redflags');

IF @tabId IS NULL
BEGIN
    PRINT 'No tab with Hidden_Value = ''KYC Manual Redflags'' found — nothing to do.';
    RETURN;
END

-- 1. Deactivate the product→tab MAPPING (this is what removes the tab from
--    /FormDefinition; the handler filters mappings on Is_Active = 1).
UPDATE dbo.TmX_Entity_Tab_Product_Mapping
SET    Is_Active         = 0,
       Last_Updated_By   = @who,
       Last_Updated_Date = @now
WHERE  Tab_Id    = @tabId
  AND  Is_Active = 1;

PRINT CONCAT('KYC Manual Redflags mapping: deactivated ', @@ROWCOUNT, ' row(s).');

-- 2. Deactivate the tab row itself (semantic consistency).
UPDATE dbo.TmX_Tab
SET    Active_Flag       = 0,
       Last_Updated_By   = @who,
       Last_Updated_Date = @now
WHERE  Tab_Id      = @tabId
  AND  Active_Flag = 1;

PRINT CONCAT('KYC Manual Redflags tab row: deactivated ', @@ROWCOUNT, ' row(s).');

PRINT '';
PRINT 'Post-state:';
SELECT  m.Entity_Tab_Product_Mapping_Id, m.Product_Id, m.Tab_Id, m.Is_Active AS Mapping_Active,
        t.Active_Flag AS Tab_Active, t.Tab_Name
FROM    dbo.TmX_Entity_Tab_Product_Mapping m
JOIN    dbo.TmX_Tab t ON t.Tab_Id = m.Tab_Id
WHERE   m.Tab_Id = @tabId;

GO

PRINT 'KYC Manual Redflags tab deactivation complete.';
