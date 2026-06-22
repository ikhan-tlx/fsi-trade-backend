-- =========================================================================
-- 2026_06_053_SeedPageViewPrivileges.sql
-- -------------------------------------------------------------------------
-- Authorization redesign — privilege-aware navigation.
--
-- The FE sidebar + route guard now hide any page the signed-in user lacks a
-- page-level ".View" privilege for (instead of letting them open a page and
-- hit a 403 toast on a broken screen). This migration seeds those page-level
-- privileges and wires the default grants.
--
-- Naming reconciliation: the canonical page-View codes below are the single
-- source the FE (sections.json) and the BE read-endpoint [RequiresPrivilege]
-- guards both key off.
--
-- Grant strategy (deliberately coarse + safe — see AUTHORIZATION_REDESIGN_PLAN.md):
--   * OPERATIONAL pages (Dashboard, Inbox, Transactions, Reports, Requests,
--     Masterdata, Cases) are granted to EVERY role. Rationale: the app showed
--     these to everyone before; Active_Flag is not enforced on the RBAC tables
--     (so even "inactive" roles like AUTHORIZER IMPORT still resolve), and a
--     user whose only role missed these would get an empty sidebar / be locked
--     out. Universal grant avoids that while the *restriction* that matters —
--     the ADMIN pages — comes from the admin privileges below staying
--     IT-Admin-only.
--   * ADMIN pages stay restricted: Users.View / Roles.View / Workflow.View
--     are already IT-Admin-only (rbac_grants.sql); FieldSetup.View (new) is
--     granted to IT Admin only here. So non-admins no longer SEE User
--     management, Role management, Process Designer or Field Configuration.
--
-- Action-level grants (Transactions.Create/Edit/Resubmit, Flags.Manage,
-- Roles.Manage, Users.*, …) are NOT touched here — they already exist and keep
-- gating the in-page buttons.
--
-- Idempotent: privilege rows guarded by NOT EXISTS on Privilege_Name; grants
-- guarded by NOT EXISTS on (Role_ID, Privilege_ID). Safe to re-run.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_06_053)';

-- ---------- 1. Page-View privilege rows ----------
DECLARE @new TABLE (Code nvarchar(100) PRIMARY KEY, Description nvarchar(100));
INSERT @new(Code, Description) VALUES
    (N'Dashboard.View',      N'See the Dashboard page'),
    (N'Inbox.View',          N'See the Inbox page'),
    (N'Transactions.View',   N'See the Transactions / Trade Repository page'),
    (N'Reports.View',        N'See the Reports page'),
    (N'Requests.View',       N'See the Requests (maker/checker) page'),
    (N'Masterdata.View',     N'See the Setup Data pages (branches, location, products)'),
    (N'CaseManagement.View', N'See the Cases (CMS) page'),
    (N'FieldSetup.View',     N'See the Field Configuration page');

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

PRINT CONCAT('Section 1 — page-View privilege rows: inserted ', @@ROWCOUNT, ' new row(s).');
GO

-- ---------- 2. Universal operational grants (every role) ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_06_053)';

DECLARE @operational TABLE (Code nvarchar(100) PRIMARY KEY);
INSERT @operational(Code) VALUES
    (N'Dashboard.View'),
    (N'Inbox.View'),
    (N'Transactions.View'),
    (N'Reports.View'),
    (N'Requests.View'),
    (N'Masterdata.View'),
    (N'CaseManagement.View');

INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID,        Role_ID,         Privilege_ID,
     Active_Flag,      Effective_Start_Date, Effective_End_Date,
     Created_By,       Created_Date,
     Last_Updated_By,  Last_Updated_Date)
SELECT
    @tenantId,         r.Role_ID,       p.Privilege_ID,
    1,                 @now,            @far,
    @who,              @now,
    NULL,              NULL
FROM       dbo.TmX_Role      r
CROSS JOIN dbo.TmX_Privilege p
JOIN       @operational o ON o.Code = p.Privilege_Name
WHERE  NOT EXISTS (
    SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
    WHERE  rpm.Role_ID = r.Role_ID AND rpm.Privilege_ID = p.Privilege_ID);

PRINT CONCAT('Section 2 — universal operational grants: inserted ', @@ROWCOUNT, ' new (role,privilege) row(s).');
GO

-- ---------- 3. Admin-only page: FieldSetup.View → IT Admin ----------
DECLARE @itAdminRoleId int = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'IT Admin');

IF @itAdminRoleId IS NULL
BEGIN
    PRINT '!! Role "IT Admin" not found — grant FieldSetup.View via the role-management screen.';
    RETURN;
END

DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_06_053)';

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
WHERE  p.Privilege_Name = N'FieldSetup.View'
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
        WHERE  rpm.Role_ID = @itAdminRoleId AND rpm.Privilege_ID = p.Privilege_ID);

PRINT CONCAT('Section 3 — FieldSetup.View → IT Admin: inserted ', @@ROWCOUNT, ' new row(s).');
GO

PRINT '2026_06_053 complete — page-View privileges seeded; operational pages universal, admin pages restricted.';
