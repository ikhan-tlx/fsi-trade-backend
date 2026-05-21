-- =========================================================================
-- rbac_grants.sql — LIVING document. Re-runnable. Idempotent.
--
-- Single source of truth for the **default** RBAC baseline:
--   1. Every privilege code referenced by a [RequiresPrivilege("...")]
--      attribute on the FSI.Trade.Compliance backend.
--   2. The wiring of those privileges to the legacy "IT Admin" role so the
--      backend works out-of-the-box without leaning on the
--      Auth:BootstrapAdminRoles escape hatch.
--
-- Re-run this file on:
--   • Initial deployment (first time the new backend is brought up).
--   • Every subsequent deployment that adds a new [RequiresPrivilege] endpoint.
--   • Any time you suspect grants have been lost (e.g. a row got deactivated
--     in TmX_Role_Privilege_Mapping by accident).
--
-- =========================================================================
-- Adding a new privilege? — engineering workflow
-- =========================================================================
-- 1. Add the [RequiresPrivilege("Module.Action")] attribute to the new action
--    in the API project.
-- 2. In Section 1 below, append:
--      (N'Module.Action', N'Human-readable description of the privilege')
-- 3. In Section 2 below, append the grant block:
--      EXEC #GrantToITAdmin N'Module.Action';
-- 4. Run this file on the target environment(s).
-- 5. Verify: login as IT Admin → call the new endpoint → 200.
-- 6. If other roles should also have it, either grant via the role-edit UI
--    (when Slice 2 Step 3 ships) or add a sibling helper (#GrantToRole) call.
--
-- =========================================================================
-- Active_Flag note
-- =========================================================================
-- Rows are inserted with Active_Flag = 1 even though the new backend
-- currently ignores Active_Flag on read (per FSI direction, May 2026).
-- Setting it correctly future-proofs the seed for when lifecycle semantics
-- are formalised. Tracked in BACKLOG.md.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now  datetime      = GETUTCDATE();
DECLARE @far  datetime      = '9999-12-31';
DECLARE @who  nvarchar(200) = N'System (rbac_grants.sql)';

PRINT CONCAT('Tenant_ID for new rows: ', @tenantId);

-- =========================================================================
-- Section 1 — Privilege rows
-- =========================================================================
-- Idempotent: each row guarded by NOT EXISTS on Privilege_Name.
-- =========================================================================

DECLARE @scoped TABLE (Code nvarchar(100) PRIMARY KEY, Description nvarchar(100));
INSERT @scoped(Code, Description) VALUES
    -- Users module
    (N'Users.View',        N'View user list and individual user records'),
    (N'Users.Create',      N'Create new user accounts'),
    (N'Users.Update',      N'Modify existing user accounts'),
    (N'Users.Activate',    N'Activate or deactivate user accounts'),
    (N'Users.UnlockUser',  N'Release a locked-out user account'),

    -- Roles module
    (N'Roles.View',        N'View role list and individual role records'),
    (N'Roles.Manage',      N'Create, modify, delete roles and edit privilege grants'),

    -- Privileges module
    (N'Privileges.View',   N'List the privileges defined in the system'),

    -- Workflow module (Slice 5)
    (N'Workflow.View',     N'View workflow schemes and product mappings'),
    (N'Workflow.Manage',   N'Edit workflow schemes, product mappings, and use the visual designer'),

    -- Flags module (Slice 8)
    (N'Flags.View',        N'View flag catalogue, per-transaction flag state, scopes and stats'),
    (N'Flags.Manage',      N'Create / edit / activate / deactivate catalogue flags and manage their (product, tab) scopes');

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
FROM   @scoped s
WHERE  NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Privilege p
    WHERE  p.Privilege_Name = s.Code
);

PRINT CONCAT('Section 1 — privilege rows: inserted ', @@ROWCOUNT, ' new row(s).');

GO

-- =========================================================================
-- Section 2 — Role grants (IT Admin gets everything by default)
-- =========================================================================
-- Idempotent helper: only inserts a Role_Privilege_Mapping row if the same
-- (role, privilege) pair doesn't already exist.
-- =========================================================================

DECLARE @itAdminRoleId int = (SELECT TOP 1 Role_ID
                              FROM   dbo.TmX_Role
                              WHERE  Role_Name = N'IT Admin');

IF @itAdminRoleId IS NULL
BEGIN
    PRINT '!! Role "IT Admin" not found in TmX_Role — skipping Section 2 grants.';
    PRINT '   Privilege rows seeded; grants must be wired via UI / manual SQL.';
    RETURN;
END

DECLARE @tenantId2  int           = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId2 IS NULL SET @tenantId2 = 1;

DECLARE @now2 datetime      = GETUTCDATE();
DECLARE @far2 datetime      = '9999-12-31';
DECLARE @who2 nvarchar(100) = N'System (rbac_grants.sql)';

DECLARE @grants TABLE (PrivCode nvarchar(100) PRIMARY KEY);
INSERT @grants(PrivCode) VALUES
    -- Every code in Section 1 should appear here too — IT Admin gets all of them.
    -- New entries: append BOTH in Section 1 AND here.
    (N'Users.View'),
    (N'Users.Create'),
    (N'Users.Update'),
    (N'Users.Activate'),
    (N'Users.UnlockUser'),
    (N'Roles.View'),
    (N'Roles.Manage'),
    (N'Privileges.View'),
    (N'Workflow.View'),
    (N'Workflow.Manage'),
    (N'Flags.View'),
    (N'Flags.Manage');

INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID,             Role_ID,           Privilege_ID,
     Active_Flag,           Effective_Start_Date, Effective_End_Date,
     Created_By,            Created_Date,
     Last_Updated_By,       Last_Updated_Date)
SELECT
    @tenantId2,             @itAdminRoleId,    p.Privilege_ID,
    1,                      @now2,             @far2,
    @who2,                  @now2,
    NULL,                   NULL
FROM   @grants g
JOIN   dbo.TmX_Privilege p ON p.Privilege_Name = g.PrivCode
WHERE  NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Role_Privilege_Mapping rpm
    WHERE  rpm.Role_ID      = @itAdminRoleId
      AND  rpm.Privilege_ID = p.Privilege_ID
);

PRINT CONCAT('Section 2 — IT Admin grants: inserted ', @@ROWCOUNT, ' new row(s).');

PRINT '';
PRINT 'IT Admin currently holds these privilege codes:';
SELECT  p.Privilege_Name, p.Privilege_Description
FROM    dbo.TmX_Role_Privilege_Mapping rpm
JOIN    dbo.TmX_Privilege p ON p.Privilege_ID = rpm.Privilege_ID
WHERE   rpm.Role_ID = @itAdminRoleId
ORDER   BY p.Privilege_Name;

GO

PRINT '';
PRINT 'rbac_grants.sql — done.';
