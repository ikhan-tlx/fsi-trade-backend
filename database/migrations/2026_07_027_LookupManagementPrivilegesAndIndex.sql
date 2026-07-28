-- =========================================================================
-- 2026_07_027_LookupManagementPrivilegesAndIndex.sql
-- -------------------------------------------------------------------------
-- Lookup Management module (/admin/lookups) — see
-- LOOKUP_MANAGEMENT_MODULE_DESIGN_AND_PLAN.md. This module maintains only the
-- VALUES of a curated, field-bound, allow-listed set of TmX_Lookup types
-- (never the types themselves). Writes are maker-checker via the existing
-- Requests queue (TmX_Request, request-type "0002").
--
-- This migration:
--   1. Seeds the two module privileges (mirrors ReferenceLists.View/.Manage,
--      seeded in rbac_grants.sql / migration 2026_06_053 style):
--        * LookupManagement.View   — gates all GETs + the nav item.
--        * LookupManagement.Manage — gates all writes (submit for approval).
--   2. Grants both to the "IT Admin" role (default baseline).
--   3. Adds a FILTERED UNIQUE INDEX enforcing unique Hidden_Value within
--      (Lookup_Type, Parent_Lookup_ID) for ACTIVE rows (plan §4.2). The
--      handler also validates this for a friendly message; the index is the
--      hard backstop.
--
-- Idempotent: privilege rows guarded by NOT EXISTS on Privilege_Name; the
-- grant guarded by NOT EXISTS on (Role_ID, Privilege_ID); the index guarded by
-- NOT EXISTS on sys.indexes AND a duplicate pre-check (skips + warns instead of
-- failing the migration if legacy active duplicates exist). Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_027_LookupManagementPrivilegesAndIndex.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- filtered indexes require QUOTED_IDENTIFIER ON
GO

-- ---------- 1. Privilege rows ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_027)';

DECLARE @new TABLE (Code nvarchar(100) PRIMARY KEY, Description nvarchar(100));
INSERT @new(Code, Description) VALUES
    (N'LookupManagement.View',   N'View managed lookup types, values and their "where used"'),
    (N'LookupManagement.Manage', N'Submit lookup-value add / edit / activate-deactivate / delete for approval');

INSERT INTO dbo.TmX_Privilege
    (Tenant_ID,
     Privilege_Name,        Privilege_Description,
     Active_Flag,           Effective_Start_Date, Effective_End_Date,
     Created_By,            Created_Date,
     Last_Updated_By,       Last_Updated_Date)
SELECT
    @tenantId,
    s.Code,                 s.Description,
    1,                      @now,                @far,
    @who,                   @now,
    @who,                   @now
FROM   @new s
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Privilege p WHERE p.Privilege_Name = s.Code);

PRINT CONCAT('Section 1 - LookupManagement privilege rows: inserted ', @@ROWCOUNT, ' new row(s).');
GO

-- ---------- 2. Grant to IT Admin (default baseline) ----------
DECLARE @itAdminRoleId int = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'IT Admin');

IF @itAdminRoleId IS NULL
BEGIN
    PRINT '!! Role "IT Admin" not found - grant LookupManagement.View/.Manage via the role-management screen.';
END
ELSE
BEGIN
    DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
    IF @tenantId IS NULL SET @tenantId = 1;
    DECLARE @now datetime      = GETUTCDATE();
    DECLARE @far datetime      = '9999-12-31';
    DECLARE @who nvarchar(100) = N'System (2026_07_027)';

    INSERT INTO dbo.TmX_Role_Privilege_Mapping
        (Tenant_ID,        Role_ID,            Privilege_ID,
         Active_Flag,      Effective_Start_Date, Effective_End_Date,
         Created_By,       Created_Date,
         Last_Updated_By,  Last_Updated_Date)
    SELECT
        @tenantId,         @itAdminRoleId,     p.Privilege_ID,
        1,                 @now,               @far,
        @who,              @now,
        NULL,              NULL
    FROM   dbo.TmX_Privilege p
    WHERE  p.Privilege_Name IN (N'LookupManagement.View', N'LookupManagement.Manage')
      AND  NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
            WHERE  rpm.Role_ID = @itAdminRoleId AND rpm.Privilege_ID = p.Privilege_ID);

    PRINT CONCAT('Section 2 - LookupManagement grants to IT Admin: inserted ', @@ROWCOUNT, ' new row(s).');
END
GO

-- ---------- 3. Filtered unique index (active rows) — SCOPED to managed types ----------
-- Unique Hidden_Value within (Lookup_Type, Parent_Lookup_ID) for ACTIVE rows,
-- but ONLY for the allow-listed / managed lookup types. A table-wide index is
-- WRONG here: HSPriceList (and other structural lookups) legitimately carry
-- duplicate (Lookup_Type, Parent_Lookup_ID, Hidden_Value) rows — verified 664
-- such groups, all HSPriceList — because those rows differ by other columns.
-- The managed types (which this module edits) were verified duplicate-free, so
-- the scoped index creates cleanly and enforces uniqueness exactly where writes
-- happen. Keep this IN-list in sync with LookupAdminAllowList.Types (C#).
-- (App-layer validation in the write handler remains the primary guard; this is
--  defence-in-depth for the managed slice only.)
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UX_TmX_Lookup_Managed_Type_Parent_Hidden'
             AND object_id = OBJECT_ID('dbo.TmX_Lookup'))
BEGIN
    PRINT 'Section 3 - UX_TmX_Lookup_Managed_Type_Parent_Hidden already exists - skipped.';
END
ELSE
BEGIN
    DECLARE @dups int = (
        SELECT COUNT(*)
        FROM (
            SELECT Lookup_Type, Parent_Lookup_ID, Hidden_Value
            FROM   dbo.TmX_Lookup
            WHERE  Is_Active = 1 AND Hidden_Value IS NOT NULL
              AND  Lookup_Type IN (
                   'Nationality','PEP','TradeNationality','LOB','termofpayment','Currency',
                   'Industry','CustTranThrough','Port','ExpiryType','IncotermCust','ESI_YES_NO_NA',
                   'PerfCust','tradefacilitycustomer','TradeProdFac','Document','BusinessStructure',
                   'CustWithlocal','Confirmation','Province','Charges','UndertakeForm','DoesCustIntend',
                   'Delivery','UnitofMeasure','ApplicableRule','FileIdent','ModeOfShipment','DemandState',
                   'Yes_No','Year','dealingwithDefenseAndLocal','BranchCode','Purpose','CommonUOM',
                   'CustomerIndustrySector')
            GROUP  BY Lookup_Type, Parent_Lookup_ID, Hidden_Value
            HAVING COUNT(*) > 1
        ) d);

    IF @dups > 0
    BEGIN
        PRINT CONCAT('!! Section 3 - SKIPPED: ', @dups,
                     ' active duplicate group(s) within MANAGED types. Resolve, then re-run.');
        SELECT Lookup_Type, Parent_Lookup_ID, Hidden_Value, COUNT(*) AS RowCount_
        FROM   dbo.TmX_Lookup
        WHERE  Is_Active = 1 AND Hidden_Value IS NOT NULL
          AND  Lookup_Type IN (
               'Nationality','PEP','TradeNationality','LOB','termofpayment','Currency',
               'Industry','CustTranThrough','Port','ExpiryType','IncotermCust','ESI_YES_NO_NA',
               'PerfCust','tradefacilitycustomer','TradeProdFac','Document','BusinessStructure',
               'CustWithlocal','Confirmation','Province','Charges','UndertakeForm','DoesCustIntend',
               'Delivery','UnitofMeasure','ApplicableRule','FileIdent','ModeOfShipment','DemandState',
               'Yes_No','Year','dealingwithDefenseAndLocal','BranchCode','Purpose','CommonUOM',
               'CustomerIndustrySector')
        GROUP  BY Lookup_Type, Parent_Lookup_ID, Hidden_Value
        HAVING COUNT(*) > 1;
    END
    ELSE
    BEGIN
        CREATE UNIQUE INDEX UX_TmX_Lookup_Managed_Type_Parent_Hidden
            ON dbo.TmX_Lookup (Lookup_Type, Parent_Lookup_ID, Hidden_Value)
            WHERE Is_Active = 1 AND Hidden_Value IS NOT NULL
              AND Lookup_Type IN (
                   'Nationality','PEP','TradeNationality','LOB','termofpayment','Currency',
                   'Industry','CustTranThrough','Port','ExpiryType','IncotermCust','ESI_YES_NO_NA',
                   'PerfCust','tradefacilitycustomer','TradeProdFac','Document','BusinessStructure',
                   'CustWithlocal','Confirmation','Province','Charges','UndertakeForm','DoesCustIntend',
                   'Delivery','UnitofMeasure','ApplicableRule','FileIdent','ModeOfShipment','DemandState',
                   'Yes_No','Year','dealingwithDefenseAndLocal','BranchCode','Purpose','CommonUOM',
                   'CustomerIndustrySector');
        PRINT 'Section 3 - created UX_TmX_Lookup_Managed_Type_Parent_Hidden (scoped to managed types).';
    END
END
GO

PRINT '2026_07_027 complete - LookupManagement privileges seeded + managed-scope unique index ensured.';
GO
