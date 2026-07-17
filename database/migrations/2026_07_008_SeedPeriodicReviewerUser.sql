/* ============================================================================
   2026_07_008_SeedPeriodicReviewerUser
   ----------------------------------------------------------------------------
   Seeds the PERIODIC_REVIEWER service principal used by the Periodic Customer
   Review flow as the in-process workflow actor + Created_By. It creates the new
   review KYC (so it satisfies the scheme's "Analyst KYC" = IsCreator maker rule)
   and, holding the "Authorizer KYC" role, satisfies the CheckRole checker rule —
   letting one principal act as maker AND checker with NO scheme (XML) change.

   The service user never logs in (PasswordHash/SecurityStamp left NULL). All
   inserts are guarded (idempotent). Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

DECLARE @Now DATETIME = GETUTCDATE(), @End DATETIME = '9999-12-31';
DECLARE @Uid NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';   -- static reviewer GUID (workflow identity must be a GUID)

-- 1. Service user.
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_User WHERE User_ID = @Uid)
    INSERT dbo.TmX_User
        (User_ID, Tenant_ID, User_Name, Active_Flag, Effective_Start_Date, Effective_End_Date,
         Created_By, Created_Date, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled,
         LockoutEnabled, AccessFailedCount, FirstPasswordChange, Status)
    VALUES
        (@Uid, 1, N'PERIODIC REVIEWER', 1, @Now, @End,
         N'SYSTEM', @Now, 0, 0, 0,
         0, 0, 0, N'Active');
GO

-- 2. Ensure the 'Authorizer KYC' role exists (should already; seed if missing).
DECLARE @Now2 DATETIME = GETUTCDATE(), @End2 DATETIME = '9999-12-31';
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Role WHERE Role_Name = N'Authorizer KYC')
    INSERT dbo.TmX_Role
        (Tenant_ID, Role_Name, Role_Description, Active_Flag, Effective_Start_Date, Effective_End_Date, Created_By, Created_Date)
    VALUES
        (1, N'Authorizer KYC', N'KYC Authorizer', 1, @Now2, @End2, N'SYSTEM', @Now2);
GO

-- 3. Map the service user to the 'Authorizer KYC' role (idempotent).
DECLARE @Now3 DATETIME = GETUTCDATE(), @End3 DATETIME = '9999-12-31';
DECLARE @Uid3 NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';
DECLARE @RoleId INT = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'Authorizer KYC' ORDER BY Role_ID);

IF @RoleId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TmX_User_Role_Mapping WHERE User_Id = @Uid3 AND Role_Id = @RoleId)
    INSERT dbo.TmX_User_Role_Mapping
        (Tenant_Id, User_Id, Role_Id, Effective_Start_Date, Effective_End_Date, Active_Flag, Created_By, Created_Date)
    VALUES
        (1, @Uid3, @RoleId, @Now3, @End3, 1, N'SYSTEM', @Now3);
GO

-- 4. Map the service user to ALL company branches (the create handler requires
--    the creating user to be actively mapped to the transaction's branch).
DECLARE @Now4 DATETIME = GETUTCDATE(), @End4 DATETIME = '9999-12-31';
DECLARE @Uid4 NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';
INSERT dbo.TmX_Company_Branch_Users_Mapping
    (Company_Branch_ID, Tenant_ID, User_Id, Effective_Start_Date, Effective_End_Date, Created_Date, Created_By, Active_Flag)
SELECT cb.Company_Branch_ID, 1, @Uid4, @Now4, @End4, @Now4, N'SYSTEM', 1
FROM   dbo.TmX_Company_Branch cb
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Company_Branch_Users_Mapping m
                   WHERE m.User_Id = @Uid4 AND m.Company_Branch_ID = cb.Company_Branch_ID);
GO
