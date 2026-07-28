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

-- IMPORTANT: TmX_Privilege.Privilege_Description is nvarchar(100).
-- Keep every Description value below at <= 100 characters or the
-- INSERT will throw "String or binary data would be truncated". Code
-- column is also nvarchar(100), but in practice privilege codes stay
-- well under that.
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
    (N'Flags.Manage',      N'Create / edit / activate / deactivate catalogue flags and manage their (product, tab) scopes'),

    -- Reference Lists module (normalized Types / Values / M:N — migration 2026_07_023)
    (N'ReferenceLists.View',   N'View reference types, values and their memberships'),
    (N'ReferenceLists.Manage', N'Create / edit / delete reference types, values and memberships'),

    -- Lookup Management module (values of allow-listed lookup types — migration 2026_07_027)
    (N'LookupManagement.View',   N'View managed lookup types, values and their "where used"'),
    (N'LookupManagement.Manage', N'Submit lookup-value add / edit / activate-deactivate / delete for approval'),

    -- Transactions module (Slice 9)
    (N'Transactions.Resubmit',       N'Manually re-fire the FCCM trade submission for a transaction'),

    -- Transactions module (Slice 10 §7.4 + §7.5)
    (N'Transactions.AddNote',        N'Append a note / remark to a transaction'),
    (N'Transactions.UploadDocument', N'Attach a document directly to a transaction'),

    -- Transactions data entry (Slice 14 — maker/checker; analysts excluded)
    (N'Transactions.Create',         N'Create a new transaction (data entry)'),
    (N'Transactions.Edit',           N'Edit an existing transaction''s data (the edit form)'),

    -- FieldSetup module (Slice 12 §A)
    (N'FieldSetup.Manage',           N'Edit dynamic-form field config (label, placeholder, visible, required)'),

    -- Page-View privileges (authorization redesign 2026-06-22 — migration 2026_06_053)
    (N'Dashboard.View',              N'See the Dashboard page'),
    (N'Inbox.View',                  N'See the Inbox page'),
    (N'Transactions.View',           N'See the Transactions / Trade Repository page'),
    (N'Reports.View',                N'See the Reports page'),
    (N'Requests.View',               N'See the Requests (maker/checker) page'),
    (N'Masterdata.View',             N'See the Setup Data pages (branches, location, products)'),
    (N'CaseManagement.View',         N'See the Cases (CMS) page'),
    (N'FieldSetup.View',             N'See the Field Configuration page');

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
    (N'Flags.Manage'),
    (N'ReferenceLists.View'),
    (N'ReferenceLists.Manage'),
    (N'LookupManagement.View'),
    (N'LookupManagement.Manage'),
    (N'Transactions.Resubmit'),
    (N'Transactions.AddNote'),
    (N'Transactions.UploadDocument'),
    (N'Transactions.Create'),
    (N'Transactions.Edit'),
    (N'FieldSetup.Manage'),
    -- Page-View privileges — IT Admin holds all (authorization redesign 2026-06-22)
    (N'Dashboard.View'),
    (N'Inbox.View'),
    (N'Transactions.View'),
    (N'Reports.View'),
    (N'Requests.View'),
    (N'Masterdata.View'),
    (N'CaseManagement.View'),
    (N'FieldSetup.View');

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

-- =========================================================================
-- Section 3 — Universal grants (EVERY role gets these)
-- =========================================================================
-- Some privileges back endpoints the FE calls during bootstrap regardless
-- of role. If a non-IT-Admin user misses these, the SPA fails to render
-- on first page load. Currently:
--
--   Privileges.View → GET /api/v1/Privilege/Entities
--                     (FE reads the privilege catalog so it can render
--                      permission gates client-side; called on login.
--                      Analysts / makers / authorizers all hit this.)
--
-- Pattern: CROSS JOIN every role with every universal privilege; the
-- NOT EXISTS guard skips pairs that are already granted. Idempotent;
-- safe to re-run after creating new roles.
--
-- IMPORTANT: when adding a new privilege here, also add it to Section 1
-- so the privilege row exists before the grant tries to reference it.
-- =========================================================================

DECLARE @tenantId3 int          = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId3 IS NULL SET @tenantId3 = 1;

DECLARE @now3 datetime           = GETUTCDATE();
DECLARE @far3 datetime           = '9999-12-31';
DECLARE @who3 nvarchar(100)      = N'System (rbac_grants.sql - Section 3)';

DECLARE @universal TABLE (PrivCode nvarchar(100) PRIMARY KEY);
INSERT @universal(PrivCode) VALUES
    -- FE bootstrap requirement — see header comment above.
    (N'Privileges.View'),
    -- Operational page-View privileges — every role sees these pages
    -- (authorization redesign 2026-06-22). Admin pages stay IT-Admin-only
    -- (Users/Roles/Workflow in Section 2; FieldSetup.View not universal).
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
    @tenantId3,        r.Role_ID,       p.Privilege_ID,
    1,                 @now3,           @far3,
    @who3,             @now3,
    NULL,              NULL
FROM     dbo.TmX_Role r
CROSS JOIN dbo.TmX_Privilege p
JOIN     @universal u ON u.PrivCode = p.Privilege_Name
WHERE    NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Role_Privilege_Mapping rpm
    WHERE  rpm.Role_ID      = r.Role_ID
      AND  rpm.Privilege_ID = p.Privilege_ID
);

PRINT CONCAT('Section 3 — universal grants: inserted ', @@ROWCOUNT, ' new row(s).');

GO

PRINT '';
PRINT 'rbac_grants.sql — done.';
