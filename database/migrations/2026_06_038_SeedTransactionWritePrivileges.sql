-- =========================================================================
-- 2026_06_038_SeedTransactionWritePrivileges.sql
-- -------------------------------------------------------------------------
-- Slice 14 — restrict transaction data entry by privilege.
--
-- Until now POST /Transaction (create) and PUT /Transaction/{id} (edit) were
-- only [Authorize] — ANY authenticated user (incl. analysts) could create and
-- edit transaction data. Per ICBC's maker/checker requirement the analyst must
-- be view-only on data; only the Authorizer (and IT Admin) may create/edit.
--
-- This migration:
--   1. Seeds two privilege codes — Transactions.Create, Transactions.Edit.
--   2. Grants BOTH to the "IT Admin" and "Authorizer" roles (by name).
-- Analysts are simply NOT granted them, so the [RequiresPrivilege] guards on
-- the two endpoints (added in the API) reject their writes with 403.
--
-- Idempotent: privilege rows guarded by NOT EXISTS on Privilege_Name; grants
-- guarded by NOT EXISTS on (Role_ID, Privilege_ID). Safe to re-run. Grants are
-- also editable afterwards in the role-management screen.
-- =========================================================================

SET NOCOUNT ON;

DECLARE @tenantId int          = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now datetime          = GETUTCDATE();
DECLARE @far datetime          = '9999-12-31';
DECLARE @who nvarchar(100)     = N'System (2026_06_038)';

-- ---------- 1. Privilege rows ----------
DECLARE @new TABLE (Code nvarchar(100) PRIMARY KEY, Description nvarchar(100));
INSERT @new(Code, Description) VALUES
    (N'Transactions.Create', N'Create a new transaction (data entry)'),
    (N'Transactions.Edit',   N'Edit an existing transaction''s data (the edit form)');

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

PRINT CONCAT('Privilege rows: inserted ', @@ROWCOUNT, ' new row(s).');

-- ---------- 2. Grants to IT Admin + Authorizer (by role name) ----------
DECLARE @roleNames TABLE (Name nvarchar(256) PRIMARY KEY);
INSERT @roleNames(Name) VALUES (N'IT Admin'), (N'Authorizer');

INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID,             Role_ID,           Privilege_ID,
     Active_Flag,           Effective_Start_Date, Effective_End_Date,
     Created_By,            Created_Date,
     Last_Updated_By,       Last_Updated_Date)
SELECT
    @tenantId,             r.Role_ID,          p.Privilege_ID,
    1,                     @now,               @far,
    @who,                  @now,
    NULL,                  NULL
FROM       dbo.TmX_Privilege p
CROSS JOIN dbo.TmX_Role      r
JOIN       @roleNames rn ON rn.Name = r.Role_Name
WHERE  p.Privilege_Name IN (N'Transactions.Create', N'Transactions.Edit')
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
        WHERE  rpm.Role_ID = r.Role_ID AND rpm.Privilege_ID = p.Privilege_ID);

PRINT CONCAT('Role grants: inserted ', @@ROWCOUNT, ' new (role,privilege) mapping(s).');

-- Visibility: warn about any target role that doesn't exist in this tenant so
-- the operator knows to grant via the role-management UI instead.
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Role WHERE Role_Name = N'Authorizer')
    PRINT '!! Role "Authorizer" not found — grant Transactions.Create/Edit to the appropriate role via the role-management screen.';
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Role WHERE Role_Name = N'IT Admin')
    PRINT '!! Role "IT Admin" not found — grant Transactions.Create/Edit via the role-management screen.';

PRINT '2026_06_038 complete — analysts are now blocked from create/edit; Authorizer + IT Admin granted.';
