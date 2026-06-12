-- =========================================================================
-- 2026_06_039_FixTransactionWritePrivilegeGrants.sql
-- -------------------------------------------------------------------------
-- Corrects 2026_06_038, which granted Transactions.Create/Edit to the
-- Authorizer (checker) role. In ICBC's maker/checker model the ANALYST is the
-- maker (creates + edits the draft) and the AUTHORIZER is the checker (reviews
-- + approves; no data entry). So the grants were reversed.
--
-- This migration:
--   • GRANTS Transactions.Create + Transactions.Edit to the maker roles
--     Analyst, ANALYST IMPORT, Analyst KYC.
--   • REVOKES both from the checker roles Authorizer, AUTHORIZER IMPORT,
--     Authorizer KYC.
-- IT Admin keeps both (super-admin). Idempotent: grants guarded by NOT EXISTS,
-- revokes are a guarded DELETE. The FE gates on these codes, so after the next
-- login analysts see Add Trade / Open form and authorizers don't.
-- =========================================================================

SET NOCOUNT ON;

DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_06_039)';

-- ---------- 1. GRANT to maker (Analyst) roles ----------
DECLARE @makers TABLE (Name nvarchar(256) PRIMARY KEY);
INSERT @makers(Name) VALUES (N'Analyst'), (N'ANALYST IMPORT'), (N'Analyst KYC');

INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID,        Role_ID,        Privilege_ID,
     Active_Flag,      Effective_Start_Date, Effective_End_Date,
     Created_By,       Created_Date,
     Last_Updated_By,  Last_Updated_Date)
SELECT
    @tenantId,        r.Role_ID,       p.Privilege_ID,
    1,                @now,            @far,
    @who,             @now,
    NULL,             NULL
FROM       dbo.TmX_Privilege p
CROSS JOIN dbo.TmX_Role      r
JOIN       @makers m ON m.Name = r.Role_Name
WHERE  p.Privilege_Name IN (N'Transactions.Create', N'Transactions.Edit')
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
        WHERE  rpm.Role_ID = r.Role_ID AND rpm.Privilege_ID = p.Privilege_ID);

PRINT CONCAT('Maker grants added: ', @@ROWCOUNT, ' (role,privilege) mapping(s).');

-- ---------- 2. REVOKE from checker (Authorizer) roles ----------
DECLARE @checkers TABLE (Name nvarchar(256) PRIMARY KEY);
INSERT @checkers(Name) VALUES (N'Authorizer'), (N'AUTHORIZER IMPORT'), (N'Authorizer KYC');

DELETE rpm
FROM   dbo.TmX_Role_Privilege_Mapping rpm
JOIN   dbo.TmX_Role      r ON r.Role_ID      = rpm.Role_ID
JOIN   dbo.TmX_Privilege p ON p.Privilege_ID = rpm.Privilege_ID
JOIN   @checkers c        ON c.Name          = r.Role_Name
WHERE  p.Privilege_Name IN (N'Transactions.Create', N'Transactions.Edit');

PRINT CONCAT('Checker grants removed: ', @@ROWCOUNT, ' (role,privilege) mapping(s).');

PRINT '2026_06_039 complete — Analyst (maker) now creates/edits; Authorizer (checker) does not.';
