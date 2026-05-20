-- =========================================================================
-- 2026_05_005_SeedScopedPrivileges.sql  — SUPERSEDED
--
-- This file is kept for history. Its responsibilities (and more) have been
-- absorbed into the LIVING seed file:
--
--     database/seed/rbac_grants.sql
--
-- That file is the single source of truth for:
--   1. Privilege rows (this file's original job).
--   2. IT Admin role grants (new — turns the bootstrap escape hatch from
--      load-bearing into a break-glass-only safety net).
--
-- Re-running this migration is harmless (NOT EXISTS guards), but
-- rbac_grants.sql does the job better. Use that going forward.
--
-- Slice 2 — privilege scaffold seed. Inserts the scoped privilege codes the
-- new FSI.Trade.Compliance backend will check via [RequiresPrivilege("...")]
-- attributes. Sits ALONGSIDE the existing 8 generic-verb rows (View, Edit,
-- Delete, Create, Assign, Verify, Approve, View Entity) — does NOT modify or
-- delete them, so anything still reading the legacy strings keeps working.
--
-- Idempotent — safe to re-run. Each row guarded by NOT EXISTS on Privilege_Name.
--
-- Active_Flag: rows are inserted with Active_Flag = 1 even though the new
-- backend currently ignores Active_Flag on read (per FSI direction, May 2026).
-- Setting it correctly future-proofs the seed for when lifecycle semantics
-- are formalised.
--
-- Tenant_ID: derives from whatever the existing privilege rows use (most
-- likely 1 — the primary tenant). Falls back to 1 if the table is empty.
--
-- Run on:  ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now  datetime       = GETUTCDATE();
DECLARE @far  datetime       = '9999-12-31';
DECLARE @who  nvarchar(200)  = N'System (Slice 2 seed)';

PRINT CONCAT('Seeding scoped privileges with Tenant_ID = ', @tenantId);

DECLARE @scoped TABLE (Code nvarchar(100) PRIMARY KEY, Description nvarchar(100));
INSERT @scoped(Code, Description) VALUES
    -- Users module
    (N'Users.View',          N'View user list and individual user records'),
    (N'Users.Create',        N'Create new user accounts'),
    (N'Users.Update',        N'Modify existing user accounts'),
    (N'Users.Activate',      N'Activate or deactivate user accounts'),
    (N'Users.UnlockUser',    N'Release a locked-out user account'),

    -- Roles module
    (N'Roles.View',          N'View role list and individual role records'),
    (N'Roles.Manage',        N'Create, modify, delete roles and edit privilege grants'),

    -- Privileges module (read-only — privilege rows themselves are config, not data)
    (N'Privileges.View',     N'List the privileges defined in the system');

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

DECLARE @inserted int = @@ROWCOUNT;
PRINT CONCAT('Inserted ', @inserted, ' new privilege row(s).');

PRINT '';
PRINT 'Current scoped-privilege roster:';
SELECT  p.Privilege_ID,
        p.Privilege_Name,
        p.Privilege_Description,
        p.Active_Flag
FROM    dbo.TmX_Privilege p
JOIN    @scoped s ON s.Code = p.Privilege_Name
ORDER   BY p.Privilege_Name;

GO

PRINT 'Slice 2 privilege seed complete.';
