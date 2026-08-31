-- =========================================================================
-- 2026_08_051_FieldSetupCreatePrivilege.sql
-- -------------------------------------------------------------------------
-- Field Configuration — "Add field / Add section" capability.
--
-- Adding brand-new fields/sections is a higher-privilege action than editing
-- existing ones, so it gets its own privilege:
--   * FieldSetup.Create — add new headings / sub-text / fields / dropdowns /
--                         table columns via the Field Configuration builder.
--
-- The FE gates the "Add" / "Add section" affordances on this privilege; the
-- change still rides the existing maker/checker publish (request type 0007,
-- which itself requires FieldSetup.Manage to submit), so a maker needs BOTH
-- FieldSetup.Manage (submit) and FieldSetup.Create (see the Add affordance).
--
-- Grants FieldSetup.Create to every role that already holds FieldSetup.Manage
-- (so existing field-config admins keep working), plus IT Admin as baseline.
--
-- Idempotent: privilege row guarded by NOT EXISTS on Privilege_Name; grants by
-- NOT EXISTS on (Role_ID, Privilege_ID). Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_051_FieldSetupCreatePrivilege.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

-- ---------- 1. Privilege row ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_08_051)';

INSERT INTO dbo.TmX_Privilege
    (Tenant_ID,
     Privilege_Name,        Privilege_Description,
     Active_Flag,           Effective_Start_Date, Effective_End_Date,
     Created_By,            Created_Date,
     Last_Updated_By,       Last_Updated_Date)
SELECT
    @tenantId,
    N'FieldSetup.Create',   N'Add new fields / sections in Field Configuration',
    1,                      @now,                @far,
    @who,                   @now,
    @who,                   @now
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Privilege p WHERE p.Privilege_Name = N'FieldSetup.Create');

PRINT CONCAT('Section 1 - FieldSetup.Create privilege: inserted ', @@ROWCOUNT, ' new row(s).');
GO

-- ---------- 2. Grant to every role that has FieldSetup.Manage (+ IT Admin) ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_08_051)';

DECLARE @createPrivId int = (SELECT TOP 1 Privilege_ID FROM dbo.TmX_Privilege WHERE Privilege_Name = N'FieldSetup.Create');
DECLARE @managePrivId int = (SELECT TOP 1 Privilege_ID FROM dbo.TmX_Privilege WHERE Privilege_Name = N'FieldSetup.Manage');

-- roles that already hold FieldSetup.Manage
DECLARE @roles TABLE (Role_ID int PRIMARY KEY);
IF @managePrivId IS NOT NULL
    INSERT INTO @roles(Role_ID)
    SELECT DISTINCT rpm.Role_ID
    FROM   dbo.TmX_Role_Privilege_Mapping rpm
    WHERE  rpm.Privilege_ID = @managePrivId
      AND  rpm.Active_Flag = 1;

-- baseline: IT Admin
DECLARE @itAdminRoleId int = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'IT Admin');
IF @itAdminRoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @roles WHERE Role_ID = @itAdminRoleId)
    INSERT INTO @roles(Role_ID) VALUES (@itAdminRoleId);

IF @createPrivId IS NULL
    PRINT '!! FieldSetup.Create privilege row missing - cannot grant.';
ELSE
BEGIN
    INSERT INTO dbo.TmX_Role_Privilege_Mapping
        (Tenant_ID,        Role_ID,            Privilege_ID,
         Active_Flag,      Effective_Start_Date, Effective_End_Date,
         Created_By,       Created_Date,
         Last_Updated_By,  Last_Updated_Date)
    SELECT
        @tenantId,         r.Role_ID,          @createPrivId,
        1,                 @now,               @far,
        @who,              @now,
        NULL,              NULL
    FROM   @roles r
    WHERE  NOT EXISTS (
            SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping m
            WHERE  m.Role_ID = r.Role_ID AND m.Privilege_ID = @createPrivId);

    PRINT CONCAT('Section 2 - FieldSetup.Create grants: inserted ', @@ROWCOUNT, ' new row(s).');
END
GO

PRINT '2026_08_051 complete - FieldSetup.Create privilege seeded + granted.';
GO
