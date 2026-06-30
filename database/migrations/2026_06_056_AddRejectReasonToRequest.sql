-- =========================================================================
-- 2026_06_056_AddRejectReasonToRequest.sql
-- 2026-06-24 — maker-checker: persist the checker's reject reason / comment.
-- Adds dbo.TmX_Request.Reject_Reason (nullable). Idempotent. Run on: ICBC_DEMO
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_Request') AND name = 'Reject_Reason')
BEGIN
    ALTER TABLE dbo.TmX_Request ADD Reject_Reason NVARCHAR(1000) NULL;
    PRINT 'Added dbo.TmX_Request.Reject_Reason';
END
ELSE PRINT 'Reject_Reason already exists — no-op';
GO
