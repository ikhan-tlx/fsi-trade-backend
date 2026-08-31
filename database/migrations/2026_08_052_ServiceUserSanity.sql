-- =========================================================================
-- 2026_08_052_ServiceUserSanity.sql
-- -------------------------------------------------------------------------
-- Option A follow-up: the periodic-review identity is now a recognized SYSTEM
-- principal (FsiWorkflowRuleProvider + CreateTransactionCommandHandler bypass),
-- so it no longer needs ANY human RBAC / branch data to drive the KYC review.
--
-- This migration cleans the service account up:
--   1. DELETE its role mapping(s)            (TmX_User_Role_Mapping)
--   2. DELETE its company/branch mapping(s)  (TmX_Company_Branch_Users_Mapping)
--   3. Deactivate it                         (Active_Flag = 0, Status = 'Inactive')
--   4. Rename it 'PERIODIC REVIEWER' -> 'Service User'
--
-- Effect: it can't log in (inactive + no password), it's hidden from the Users
-- list (ListUsersQueryHandler excludes ReviewerUserId), and it holds no role or
-- branch — yet periodic review still completes because the workflow recognizes it
-- as a system principal.
--
-- Scoped strictly to the configured service GUID. Idempotent / re-runnable.
-- Run:
--   sqlcmd -S "<server>" -d <db> -E -b -i "<path>\2026_08_052_ServiceUserSanity.sql"
-- =========================================================================
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @Svc NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';  -- = appsettings PeriodicReview:ReviewerUserId
DECLARE @NewName NVARCHAR(100) = N'Service User';
DECLARE @Now DATETIME = GETUTCDATE();

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_User WHERE User_ID = @Svc)
BEGIN
    PRINT 'Service user ' + @Svc + ' not found — nothing to sanitise.';
    RETURN;
END

-- 1. Role mapping(s).
DELETE FROM dbo.TmX_User_Role_Mapping WHERE User_Id = @Svc;
PRINT 'Deleted ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' role mapping(s).';

-- 2. Company/branch mapping(s).
DELETE FROM dbo.TmX_Company_Branch_Users_Mapping WHERE User_Id = @Svc;
PRINT 'Deleted ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' branch mapping(s).';

-- 3. Deactivate + 4. rename (guarded so re-runs are no-ops once applied).
UPDATE dbo.TmX_User
   SET Active_Flag      = 0,
       Status           = N'Inactive',
       User_Name        = @NewName,
       Last_Updated_By  = N'System (2026_08_052)',
       Last_Updated_Date = @Now
 WHERE User_ID = @Svc
   AND (Active_Flag = 1 OR Status <> N'Inactive' OR User_Name <> @NewName);
PRINT 'Service account deactivated + renamed to ''' + @NewName + ''' (rows affected = ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ').';
GO

PRINT '2026_08_052 complete. The service principal has no role/branch, is Inactive, and is renamed. Periodic review runs via the Option A system-principal bypass.';
GO
