-- =========================================================================
-- 2026_07_005_KycRiskAssessmentTab.sql
-- -------------------------------------------------------------------------
-- KYC "Risk Assessment" tab (Option A).
--
-- Two additive changes, both idempotent:
--   1. Stage-transition timestamps on dbo.KycCaseRequest so the FE
--      "Processing Lifecycle" stepper can show a real time per stage.
--      The poller (FccmCaseIdPoller) stamps these on transition.
--   2. A new operational privilege Kyc.Operate that gates the tab's
--      troubleshooting actions (Retry / Re-poll / Re-fetch). Granted to
--      IT Admin + every Authorizer role by default; analysts stay read-only.
--
-- Run on:  ICBC_DEMO
-- Idempotent — guarded by COL_LENGTH / NOT EXISTS. Safe to re-run.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

-- ---------- 1. Stage-transition timestamp columns ----------
IF COL_LENGTH('dbo.KycCaseRequest', 'Case_Created_At') IS NULL
BEGIN
    ALTER TABLE dbo.KycCaseRequest ADD Case_Created_At datetime NULL;
    PRINT 'Added dbo.KycCaseRequest.Case_Created_At.';
END
ELSE PRINT 'dbo.KycCaseRequest.Case_Created_At already exists.';
GO

IF COL_LENGTH('dbo.KycCaseRequest', 'Risk_Assessed_At') IS NULL
BEGIN
    ALTER TABLE dbo.KycCaseRequest ADD Risk_Assessed_At datetime NULL;
    PRINT 'Added dbo.KycCaseRequest.Risk_Assessed_At.';
END
ELSE PRINT 'dbo.KycCaseRequest.Risk_Assessed_At already exists.';
GO

-- ---------- 2. Kyc.Operate privilege row ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_005)';

INSERT INTO dbo.TmX_Privilege
    (Tenant_ID,
     Privilege_Name,        Privilege_Description,
     Active_Flag,           Effective_Start_Date, Effective_End_Date,
     Created_By,            Created_Date,
     Last_Updated_By,       Last_Updated_Date)
SELECT
    @tenantId,
    N'Kyc.Operate',         N'Run KYC Risk-Assessment operational actions (retry / re-poll / re-fetch FCCM sync)',
    1,                      @now,                @far,
    @who,                   @now,
    @who,                   @now
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Privilege p WHERE p.Privilege_Name = N'Kyc.Operate');

PRINT CONCAT('Kyc.Operate privilege row: inserted ', @@ROWCOUNT, ' new row(s).');
GO

-- ---------- 3. Grant Kyc.Operate → IT Admin + all Authorizer roles ----------
DECLARE @tenantId int      = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_005)';

DECLARE @privId int = (SELECT TOP 1 Privilege_ID FROM dbo.TmX_Privilege WHERE Privilege_Name = N'Kyc.Operate');

INSERT INTO dbo.TmX_Role_Privilege_Mapping
    (Tenant_ID,        Role_ID,         Privilege_ID,
     Active_Flag,      Effective_Start_Date, Effective_End_Date,
     Created_By,       Created_Date,
     Last_Updated_By,  Last_Updated_Date)
SELECT
    @tenantId,         r.Role_ID,       @privId,
    1,                 @now,            @far,
    @who,              @now,
    NULL,              NULL
FROM   dbo.TmX_Role r
WHERE  (r.Role_Name = N'IT Admin' OR r.Role_Name LIKE N'%Author%')
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
        WHERE  rpm.Role_ID = r.Role_ID AND rpm.Privilege_ID = @privId);

PRINT CONCAT('Kyc.Operate grants (IT Admin + Authorizer roles): inserted ', @@ROWCOUNT, ' new (role,privilege) row(s).');
GO

PRINT '2026_07_005 complete — stage timestamps + Kyc.Operate privilege seeded.';
