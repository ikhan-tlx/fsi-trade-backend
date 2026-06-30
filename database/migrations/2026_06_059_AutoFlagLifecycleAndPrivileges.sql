-- =========================================================================
-- 2026_06_059_AutoFlagLifecycleAndPrivileges.sql
-- 2026-06-25 — Auto Flag maker-checker lifecycle + dedicated privileges.
--
--   Part A — additive lifecycle columns on TmX_Flag_Catalogue:
--     Status (Draft|PendingApproval|Active|Retired), Version_No,
--     Submitted_By/Date, Approved_By/Date, Change_Reason.
--     Backfills Status from Active_Flag for existing rows.
--   Part B — seed AutoFlags.View / AutoFlags.Author / AutoFlags.Approve
--     privileges and grant them to every role that already holds
--     'Users.View' (the admin-page gate) so current admins keep access.
--
-- Idempotent / re-runnable. QUOTED_IDENTIFIER ON for the filtered indexes
-- on TmX_Flag_Catalogue. Run on: ICBC_DEMO
-- =========================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;

-- ---------- Part A: lifecycle columns ----------
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Status') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Status NVARCHAR(20) NULL;
GO
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Version_No') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Version_No INT NULL;
GO
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Submitted_By') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Submitted_By NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Submitted_Date') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Submitted_Date DATETIME2(3) NULL;
GO
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Approved_By') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Approved_By NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Approved_Date') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Approved_Date DATETIME2(3) NULL;
GO
IF COL_LENGTH('dbo.TmX_Flag_Catalogue','Change_Reason') IS NULL
    ALTER TABLE dbo.TmX_Flag_Catalogue ADD Change_Reason NVARCHAR(1000) NULL;
GO

-- Backfill: existing rows get a Status from Active_Flag; default Version_No = 1.
UPDATE dbo.TmX_Flag_Catalogue
   SET Status = CASE WHEN Active_Flag = 1 THEN 'Active' ELSE 'Draft' END
 WHERE Status IS NULL;
UPDATE dbo.TmX_Flag_Catalogue SET Version_No = 1 WHERE Version_No IS NULL;
PRINT 'Part A — lifecycle columns ensured + backfilled.';
GO

-- ---------- Part B: AutoFlags privileges + grants ----------
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime = GETUTCDATE();
DECLARE @far datetime = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_06_059)';

DECLARE @new TABLE (Code nvarchar(100) PRIMARY KEY, Descr nvarchar(150));
INSERT @new VALUES
    (N'AutoFlags.View',    N'See the Auto Flags admin screen'),
    (N'AutoFlags.Author',  N'Create / edit auto-flag rules (maker)'),
    (N'AutoFlags.Approve', N'Approve & activate / retire auto flags (checker)');

INSERT INTO dbo.TmX_Privilege
    (Tenant_ID, Privilege_Name, Privilege_Description, Active_Flag,
     Effective_Start_Date, Effective_End_Date, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT @tenantId, s.Code, s.Descr, 1, @now, @far, @who, @now, @who, @now
FROM   @new s
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Privilege p WHERE p.Privilege_Name = s.Code);
PRINT CONCAT('Part B.1 — AutoFlags privileges inserted: ', @@ROWCOUNT);

-- Grant the three to every role that already holds 'Users.View' (admin roles),
-- so whoever can reach the admin section can use Auto Flags.
INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID, Role_ID, Privilege_ID, Active_Flag,
     Effective_Start_Date, Effective_End_Date, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT DISTINCT @tenantId, adminRoles.Role_ID, np.Privilege_ID, 1, @now, @far, @who, @now, NULL, NULL
FROM   dbo.TmX_Privilege np
CROSS JOIN (
    SELECT rpm.Role_ID
    FROM   dbo.TmX_Role_Privilege_Mapping rpm
    JOIN   dbo.TmX_Privilege p ON p.Privilege_ID = rpm.Privilege_ID
    WHERE  p.Privilege_Name = N'Users.View'
) adminRoles
WHERE  np.Privilege_Name IN (N'AutoFlags.View', N'AutoFlags.Author', N'AutoFlags.Approve')
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping x
        WHERE x.Role_ID = adminRoles.Role_ID AND x.Privilege_ID = np.Privilege_ID);
PRINT CONCAT('Part B.2 — AutoFlags grants to admin roles: ', @@ROWCOUNT);
GO

PRINT '2026_06_059 complete.';
GO
