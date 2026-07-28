-- =========================================================================
-- 2026_07_028_MakerCheckerApprovalPrivilegesAndCommandKey.sql
-- -------------------------------------------------------------------------
-- Maker/checker unification — Phase 1 (User).
--
-- Closes the two gaps in the Requests (TmX_Request) maker/checker flow:
--   (a) the generic Submit endpoint parks a change as a PENDING request, and
--   (b) approval REPLAYS the maker's original command to apply it.
--
-- This migration:
--   1. Adds TmX_Request.Command_Key — a replay marker set by the generic
--      Submit endpoint ("{type}:{action}"). Rows with a NULL Command_Key are
--      legacy/entity-shaped and are NOT auto-replayed (they keep the prior
--      status-only behaviour); the Lookup module (request-type 0002) keeps its
--      own bespoke apply path regardless.
--   2. Seeds two approval privileges (mirrors the 2026_06_053 / rbac_grants
--      style):
--        * Requests.Approve    — approve/reject OTHER people's requests (checker).
--        * Requests.ApproveOwn — additionally approve/reject your OWN request
--                                (segregation-of-duties bypass; admin only).
--   3. Grants both to the "IT Admin" role (default baseline). Other checker
--      roles get Requests.Approve via the Role Management screen.
--
-- Idempotent: column guarded by NOT EXISTS on sys.columns; privilege rows by
-- NOT EXISTS on Privilege_Name; grants by NOT EXISTS on (Role_ID, Privilege_ID).
-- Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_028_MakerCheckerApprovalPrivilegesAndCommandKey.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

-- ---------- 1. TmX_Request.Command_Key (replay marker) ----------
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.TmX_Request') AND name = 'Command_Key')
BEGIN
    ALTER TABLE dbo.TmX_Request ADD Command_Key nvarchar(80) NULL;
    PRINT 'Section 1 - added dbo.TmX_Request.Command_Key.';
END
ELSE
    PRINT 'Section 1 - dbo.TmX_Request.Command_Key already exists - skipped.';
GO

-- ---------- 2. Approval privilege rows ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_028)';

DECLARE @new TABLE (Code nvarchar(100) PRIMARY KEY, Description nvarchar(100));
INSERT @new(Code, Description) VALUES
    (N'Requests.Approve',    N'Approve or reject other users'' maker/checker requests'),
    (N'Requests.ApproveOwn', N'Approve or reject your OWN maker/checker request (admin)');

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

PRINT CONCAT('Section 2 - approval privilege rows: inserted ', @@ROWCOUNT, ' new row(s).');
GO

-- ---------- 3. Grant to IT Admin (default baseline) ----------
DECLARE @itAdminRoleId int = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'IT Admin');

IF @itAdminRoleId IS NULL
BEGIN
    PRINT '!! Role "IT Admin" not found - grant Requests.Approve / Requests.ApproveOwn via the role-management screen.';
END
ELSE
BEGIN
    DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
    IF @tenantId IS NULL SET @tenantId = 1;
    DECLARE @now datetime      = GETUTCDATE();
    DECLARE @far datetime      = '9999-12-31';
    DECLARE @who nvarchar(100) = N'System (2026_07_028)';

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
    WHERE  p.Privilege_Name IN (N'Requests.Approve', N'Requests.ApproveOwn')
      AND  NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
            WHERE  rpm.Role_ID = @itAdminRoleId AND rpm.Privilege_ID = p.Privilege_ID);

    PRINT CONCAT('Section 3 - approval grants to IT Admin: inserted ', @@ROWCOUNT, ' new row(s).');
END
GO

PRINT '2026_07_028 complete - Command_Key column + Requests.Approve/ApproveOwn privileges seeded.';
GO
