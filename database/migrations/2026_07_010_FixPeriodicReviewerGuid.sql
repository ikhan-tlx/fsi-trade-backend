/* ============================================================================
   2026_07_010_FixPeriodicReviewerGuid
   ----------------------------------------------------------------------------
   FIX (sanity E2E finding): 2026_07_008 seeded PERIODIC_REVIEWER with a
   NON-GUID primary key (User_ID = N'PERIODIC_REVIEWER'). The OptimaJet workflow
   identity system is GUID-based: WorkflowInbox.IdentityId is a uniqueidentifier
   and FsiWorkflowRuleProvider.CheckRole -> VerifyInbox does Guid.TryParse on the
   identity. A non-GUID id can never be written to the authorizer inbox, so the
   auto-driven review KYC got permanently stuck at "Sent To Authorizer"
   (SendToAuthorizer only needs IsCreator = string compare, which DID pass).

   This migration re-seeds the reviewer with a real GUID identity and repoints
   its role + branch mappings. The old non-GUID principal + its mappings are
   removed. Idempotent / re-runnable. User_Id columns are audit strings (no FK),
   so historical txns that recorded the old id are unaffected.
   ============================================================================ */

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @Now DATETIME = GETUTCDATE(), @End DATETIME = '9999-12-31';
DECLARE @Old NVARCHAR(50) = N'PERIODIC_REVIEWER';
DECLARE @Guid NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';   -- fixed reviewer GUID (matches appsettings ReviewerUserId)

-- 1. GUID service user.
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_User WHERE User_ID = @Guid)
    INSERT dbo.TmX_User
        (User_ID, Tenant_ID, User_Name, Active_Flag, Effective_Start_Date, Effective_End_Date,
         Created_By, Created_Date, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled,
         LockoutEnabled, AccessFailedCount, FirstPasswordChange, Status)
    VALUES
        (@Guid, 1, N'PERIODIC REVIEWER', 1, @Now, @End,
         N'SYSTEM', @Now, 0, 0, 0,
         0, 0, 0, N'Active');
GO

-- 2. Role mapping (Authorizer KYC) for the GUID user.
DECLARE @Now2 DATETIME = GETUTCDATE(), @End2 DATETIME = '9999-12-31';
DECLARE @Guid2 NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';
DECLARE @RoleId INT = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'Authorizer KYC' ORDER BY Role_ID);
IF @RoleId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TmX_User_Role_Mapping WHERE User_Id = @Guid2 AND Role_Id = @RoleId)
    INSERT dbo.TmX_User_Role_Mapping
        (Tenant_Id, User_Id, Role_Id, Effective_Start_Date, Effective_End_Date, Active_Flag, Created_By, Created_Date)
    VALUES
        (1, @Guid2, @RoleId, @Now2, @End2, 1, N'SYSTEM', @Now2);
GO

-- 3. Branch mappings for the GUID user (create handler requires branch membership).
DECLARE @Now3 DATETIME = GETUTCDATE(), @End3 DATETIME = '9999-12-31';
DECLARE @Guid3 NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';
INSERT dbo.TmX_Company_Branch_Users_Mapping
    (Company_Branch_ID, Tenant_ID, User_Id, Effective_Start_Date, Effective_End_Date, Created_Date, Created_By, Active_Flag)
SELECT cb.Company_Branch_ID, 1, @Guid3, @Now3, @End3, @Now3, N'SYSTEM', 1
FROM   dbo.TmX_Company_Branch cb
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Company_Branch_Users_Mapping m
                   WHERE m.User_Id = @Guid3 AND m.Company_Branch_ID = cb.Company_Branch_ID);
GO

-- 4. Fully retire any legacy non-GUID reviewer (User_ID = 'PERIODIC_REVIEWER').
--    A non-GUID User_ID breaks dbo.TmX_Transaction_VW, which joins
--    TmX_User.User_ID against the uniqueidentifier WorkflowInbox.IdentityId — one
--    non-GUID user 500s the ENTIRE transaction list + stats. So repoint EVERY FK
--    child that references it onto the GUID reviewer, then delete the old row.
DECLARE @Old4 NVARCHAR(50) = N'PERIODIC_REVIEWER';
DECLARE @New4 NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';
IF EXISTS (SELECT 1 FROM dbo.TmX_User WHERE User_ID = @Old4)
BEGIN
    DECLARE @repoint NVARCHAR(MAX) = N'';
    SELECT @repoint = @repoint + N'UPDATE ' + QUOTENAME(SCHEMA_NAME(fk.schema_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
        + N' SET ' + QUOTENAME(pc.name) + N' = @New WHERE ' + QUOTENAME(pc.name) + N' = @Old;' + CHAR(10)
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id  AND pc.column_id = fkc.parent_column_id
    JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
    WHERE fk.referenced_object_id = OBJECT_ID('dbo.TmX_User') AND rc.name = N'User_ID';
    IF @repoint <> N'' EXEC sp_executesql @repoint, N'@Old NVARCHAR(50), @New NVARCHAR(50)', @Old=@Old4, @New=@New4;
    DELETE FROM dbo.TmX_User WHERE User_ID = @Old4;
END
GO
