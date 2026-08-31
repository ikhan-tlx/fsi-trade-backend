-- =========================================================================
-- 2026_08_057_FccmPollingHelp.sql
-- -------------------------------------------------------------------------
-- Make the FCCM polling / timeout settings self-explanatory: precise one-line
-- Descriptions + Help grounded in FccmCaseIdPoller (what it actually polls).
-- Data-only, idempotent.
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_08_057)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

UPDATE dbo.TmX_Configuration_Definition
   SET Description = N'How often the poller checks Oracle FCCM for a pending case id.',
       Help_Detail = N'One tick per interval: FccmCaseIdPoller finds KYC cases still in AwaitingCaseId status (with an FccmRequestId) and asks FccmOracleReader.ReadCaseIdAsync for the FCCM-generated case id of each submitted onboarding request. It is NOT polling a URL — it queries the FCCM Oracle DB.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Integration.PollingInterval';
PRINT CONCAT('PollingInterval help: ', @@ROWCOUNT);

UPDATE dbo.TmX_Configuration_Definition
   SET Description = N'How long to poll for a case id before giving up.',
       Help_Detail = N'If FCCM has not returned a case id within this window of the onboarding submission, FccmCaseIdPoller stops polling that request and marks it timed out.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Integration.CaseIdTimeout';
PRINT CONCAT('CaseIdTimeout help: ', @@ROWCOUNT);

UPDATE dbo.TmX_Configuration_Definition
   SET Description = N'Per-call HTTP timeout for outbound FCCM / integration calls.',
       Help_Detail = N'Applied to the integration HttpClients (FCCM onboarding submission, customer-master, etc.) as the per-request timeout.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Integration.HttpTimeout';
PRINT CONCAT('HttpTimeout help: ', @@ROWCOUNT);
GO

PRINT '2026_08_057 complete - FCCM polling/timeout descriptions clarified.';
GO
