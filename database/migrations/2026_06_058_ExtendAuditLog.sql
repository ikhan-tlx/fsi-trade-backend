-- =========================================================================
-- 2026_06_058_ExtendAuditLog.sql
-- 2026-06-25 — extend the generic audit trail to a compliance-grade record.
-- Adds the standard who/what/when/where/how + integrity columns on top of
-- the base set created in 2026_06_057. Idempotent (per-column guards).
-- Run on: ICBC_DEMO
--
-- New columns:
--   Who      : User_Role, On_Behalf_Of
--   What     : Description
--   Where    : Host (server), Application (service name)
--   How/Ctx  : Session_Id, Correlation_Id, User_Agent, Status_Code,
--              Outcome (SUCCESS/FAILURE), Failure_Reason
--   Integrity: Row_Hash (SHA-256 of canonical content + prior hash),
--              Prev_Hash (hash of the immediately preceding row — chain)
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;

IF OBJECT_ID('dbo.TmX_Audit_Log','U') IS NULL
BEGIN
    RAISERROR('TmX_Audit_Log does not exist — run 2026_06_057 first.', 16, 1);
    RETURN;
END

DECLARE @added INT = 0;

IF COL_LENGTH('dbo.TmX_Audit_Log','User_Role') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD User_Role NVARCHAR(400) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','On_Behalf_Of') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD On_Behalf_Of NVARCHAR(200) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Description') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Description NVARCHAR(1000) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Host') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Host NVARCHAR(200) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Application') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Application NVARCHAR(200) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Session_Id') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Session_Id NVARCHAR(100) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Correlation_Id') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Correlation_Id NVARCHAR(100) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','User_Agent') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD User_Agent NVARCHAR(512) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Status_Code') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Status_Code INT NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Outcome') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Outcome NVARCHAR(20) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Failure_Reason') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Failure_Reason NVARCHAR(1000) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Row_Hash') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Row_Hash NVARCHAR(64) NULL; SET @added += 1; END

IF COL_LENGTH('dbo.TmX_Audit_Log','Prev_Hash') IS NULL
BEGIN ALTER TABLE dbo.TmX_Audit_Log ADD Prev_Hash NVARCHAR(64) NULL; SET @added += 1; END

PRINT CONCAT('TmX_Audit_Log extended — ', @added, ' column(s) added.');
GO

-- Helpful filter indexes for the new high-cardinality lookups.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TmX_Audit_Log_Action' AND object_id=OBJECT_ID('dbo.TmX_Audit_Log'))
    CREATE INDEX IX_TmX_Audit_Log_Action ON dbo.TmX_Audit_Log(Action);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TmX_Audit_Log_Outcome' AND object_id=OBJECT_ID('dbo.TmX_Audit_Log'))
    CREATE INDEX IX_TmX_Audit_Log_Outcome ON dbo.TmX_Audit_Log(Outcome);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TmX_Audit_Log_Correlation' AND object_id=OBJECT_ID('dbo.TmX_Audit_Log'))
    CREATE INDEX IX_TmX_Audit_Log_Correlation ON dbo.TmX_Audit_Log(Correlation_Id);
GO
