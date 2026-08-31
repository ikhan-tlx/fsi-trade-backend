-- =========================================================================
-- 2026_08_055_ConfigReview2.sql
-- -------------------------------------------------------------------------
-- Second config review pass:
--   * Integration.TradeCaseDefaults.CaseType -> free-text String (was Enum with
--     a fixed allowedValues dropdown). FCCM accepts other case-type codes, so an
--     open field is more appropriate.
--   * Workflow.InboxMaxPageSize -> DEACTIVATED. Page size is chosen from the UI;
--     the code keeps its built-in cap (WorkflowOptions default) so it need not be
--     a managed setting.
--   * Integration.CustomerMasterBaseUrl -> DEACTIVATED (DEAD). ICustomerMasterService
--     is bound to LocalCustomerMasterReader (DB-backed) in InfrastructureServiceRegistration;
--     CustomerMasterClient (the only reader of this URL) is not the wired impl and is
--     never injected -> the URL has no runtime consumer.
--   * Logging.ArchiveScanIntervalHours -> 720 (monthly). The archiver batches log
--     files older than RetentionDays (30) into one zip; running ~monthly (not every
--     12h) makes each zip hold a month of files instead of a single daily file.
--
--   NOT removed: Integration.FccmOracleSchema — it IS consumed (FccmOracleReader
--   OpenAndPrepareAsync sets ALTER SESSION CURRENT_SCHEMA from it; blank = skip).
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_055_ConfigReview2.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_08_055)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- 1) CaseType: Enum(dropdown) -> String(open field)
UPDATE dbo.TmX_Configuration_Definition
   SET Data_Type = N'String',
       Validation_Rules = NULL,
       Help_Detail = N'Bound to P_CASE_TYPE of the Oracle proc PR_CREATE_EVENT_DEMO_CFG (FccmOracleReader); the FCCM/Mantas case type code (KDD_CASES CASE_TYPE_CD). Free text so any valid FCCM case-type code can be entered.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Integration.TradeCaseDefaults.CaseType';
PRINT CONCAT('CaseType -> String: ', @@ROWCOUNT);

-- 2) Deactivate Workflow.InboxMaxPageSize (UI-controlled)
UPDATE dbo.TmX_Configuration_Definition
   SET Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Workflow.InboxMaxPageSize' AND Is_Active = 1;
PRINT CONCAT('Workflow.InboxMaxPageSize deactivated: ', @@ROWCOUNT);

-- 3) Deactivate the dead Integration.CustomerMasterBaseUrl
UPDATE dbo.TmX_Configuration_Definition
   SET Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Integration.CustomerMasterBaseUrl' AND Is_Active = 1;
PRINT CONCAT('Integration.CustomerMasterBaseUrl deactivated (dead): ', @@ROWCOUNT);

-- 4) Log archiver -> monthly cadence (720h) so zips batch a month of files
UPDATE dbo.TmX_Configuration_Definition
   SET Default_Value = N'720',
       Validation_Rules = N'{"min":1,"max":8760,"step":1}',
       Description = N'How often the log archiver runs (hours) — ~monthly.',
       Help_Detail = N'Interval at which LogArchiverService scans for rolled log files older than Logging.RetentionDays (30) and zips them. Set to 720 (30 days) so a run batches a month of files into one archive rather than a single daily file.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Logging.ArchiveScanIntervalHours';
UPDATE v
   SET v.Configuration_Value = N'720', v.Last_Updated_By = @who, v.Last_Updated_Date = @now
  FROM dbo.TmX_Configuration_Value v
  JOIN dbo.TmX_Configuration_Definition d ON d.Configuration_Definition_ID = v.Configuration_Definition_ID
 WHERE d.Configuration_Key = N'Logging.ArchiveScanIntervalHours'
   AND v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL;
PRINT 'Logging.ArchiveScanIntervalHours -> 720 (monthly).';
GO

PRINT '2026_08_055 complete - CaseType open field, InboxMaxPageSize + CustomerMasterBaseUrl deactivated, monthly archiver.';
GO
