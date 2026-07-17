/* ============================================================================
   2026_07_009_CustomerReviewHistoryAndPrivilege
   ----------------------------------------------------------------------------
   Periodic Customer Review M3:
     1. dbo.TmX_Customer_Review_History — append-only audit of each review
        attempt/outcome (Completed/Blocked/Failed/Skipped) + remark.
     2. Kyc.Review privilege — gates the admin endpoints (run / run-per-customer
        / schedule). Granted to IT Admin + all Authorizer roles.

   Additive, idempotent. Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.TmX_Customer_Review_History', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Customer_Review_History
    (
        Customer_Review_History_Id INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Customer_Review_History PRIMARY KEY,
        Tenant_Id             INT            NOT NULL CONSTRAINT DF_CRH_Tenant DEFAULT (1),
        Customer_Code         NVARCHAR(50)   NOT NULL,
        Due_Date              DATETIME2      NOT NULL,
        Outcome               NVARCHAR(30)   NOT NULL,
        Source_Transaction_Id INT            NULL,
        New_Transaction_Id    INT            NULL,
        Kyc_Request_Id        BIGINT         NULL,
        Remarks               NVARCHAR(1000) NULL,
        Created_By            NVARCHAR(100)  NOT NULL CONSTRAINT DF_CRH_CreatedBy DEFAULT ('PERIODIC REVIEWER'),
        Created_Date          DATETIME2      NOT NULL CONSTRAINT DF_CRH_CreatedDate DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CRH_Customer' AND object_id=OBJECT_ID('dbo.TmX_Customer_Review_History'))
    CREATE NONCLUSTERED INDEX IX_CRH_Customer ON dbo.TmX_Customer_Review_History (Tenant_Id, Customer_Code, Created_Date);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CRH_Outcome' AND object_id=OBJECT_ID('dbo.TmX_Customer_Review_History'))
    CREATE NONCLUSTERED INDEX IX_CRH_Outcome ON dbo.TmX_Customer_Review_History (Outcome, Created_Date);
GO

-- Kyc.Review privilege
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime = GETUTCDATE(), @far datetime = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_009)';

INSERT INTO dbo.TmX_Privilege
    (Tenant_ID, Privilege_Name, Privilege_Description,
     Active_Flag, Effective_Start_Date, Effective_End_Date,
     Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT @tenantId, N'Kyc.Review', N'Run/administer Periodic Customer Review (run sweep, per-customer run, view schedule)',
       1, @now, @far, @who, @now, @who, @now
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Privilege p WHERE p.Privilege_Name = N'Kyc.Review');
PRINT CONCAT('Kyc.Review privilege row: inserted ', @@ROWCOUNT, ' new row(s).');
GO

-- Grant Kyc.Review -> IT Admin + all Authorizer roles
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime = GETUTCDATE(), @far datetime = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_009)';
DECLARE @privId int = (SELECT TOP 1 Privilege_ID FROM dbo.TmX_Privilege WHERE Privilege_Name = N'Kyc.Review');

INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID, Role_ID, Privilege_ID,
     Active_Flag, Effective_Start_Date, Effective_End_Date,
     Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT @tenantId, r.Role_ID, @privId,
       1, @now, @far, @who, @now, NULL, NULL
FROM   dbo.TmX_Role r
WHERE  (r.Role_Name = N'IT Admin' OR r.Role_Name LIKE N'%Author%')
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm WHERE rpm.Role_ID = r.Role_ID AND rpm.Privilege_ID = @privId);
PRINT CONCAT('Kyc.Review grants: inserted ', @@ROWCOUNT, ' new (role,privilege) row(s).');
GO
