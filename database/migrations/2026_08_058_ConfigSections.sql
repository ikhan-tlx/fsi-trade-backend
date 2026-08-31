-- =========================================================================
-- 2026_08_058_ConfigSections.sql
-- -------------------------------------------------------------------------
-- Adds a Section column to TmX_Configuration_Definition (optional sub-group
-- heading within a category) and assigns the FCCM / Integration settings to
-- four self-explanatory sub-groups so the 25-row category reads clearly:
--   Oracle connection · Onboarding submission · Case defaults · Polling & timeouts
-- Settings with Section = NULL render flat (no sub-header), so other categories
-- are unchanged. Idempotent.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_058_ConfigSections.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.TmX_Configuration_Definition','Section') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Configuration_Definition ADD Section nvarchar(100) NULL;
    PRINT 'Added column Section.';
END
GO

DECLARE @who nvarchar(100) = N'System (2026_08_058)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- Oracle connection
UPDATE dbo.TmX_Configuration_Definition
   SET Section = N'Oracle connection', Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key IN (N'Integration.FccmOracleConnection', N'Integration.FccmOracleSchema');

-- Onboarding submission (the InitiateOnboarding API)
UPDATE dbo.TmX_Configuration_Definition
   SET Section = N'Onboarding submission', Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key IN (N'Integration.FccmOnboardingUrl', N'Integration.Onboarding.Username', N'Integration.Onboarding.Password');

-- Case defaults (params to the FCCM case-creation proc PR_CREATE_EVENT_DEMO_CFG)
UPDATE dbo.TmX_Configuration_Definition
   SET Section = N'Case defaults', Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key LIKE N'Integration.TradeCaseDefaults.%';

-- Polling & timeouts
UPDATE dbo.TmX_Configuration_Definition
   SET Section = N'Polling & timeouts', Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key IN (N'Integration.PollingInterval', N'Integration.CaseIdTimeout', N'Integration.HttpTimeout', N'Integration.FccmActionCodeStatusMap');

PRINT 'FCCM sub-groups assigned.';

-- Show the result
SELECT Section, COUNT(*) AS Settings
FROM   dbo.TmX_Configuration_Definition
WHERE  Category_Key = N'fccm' AND Is_Active = 1
GROUP BY Section
ORDER BY MIN(Display_Order);
GO

PRINT '2026_08_058 complete - Section column + FCCM sub-groups.';
GO
