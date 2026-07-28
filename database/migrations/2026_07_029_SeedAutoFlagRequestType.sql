-- =========================================================================
-- 2026_07_029_SeedAutoFlagRequestType.sql
-- -------------------------------------------------------------------------
-- Maker/checker unification — Phase 3 (Auto Flag) fix.
--
-- The Auto Flag full-unify (request-type 0005) parks a TmX_Request row when a
-- flag is submitted for approval (SubmitAutoFlagCommandHandler). That INSERT
-- failed at runtime with:
--
--   The INSERT statement conflicted with the FOREIGN KEY constraint
--   "FK_TmX_Requests_ReqType" ... table "dbo.TmX_Request_Type",
--   column 'Request_Type_Code'.
--
-- Root cause: dbo.TmX_Request_Type only seeded codes 0001 (User), 0002
-- (Setup Data / Lookup), 0003 (Role) and 0004 (Branch). Code 0005 (Auto Flag)
-- was never added, so the FK on TmX_Request.Request_Type_Code rejected the row.
--
-- This migration seeds the missing 0005 row. Request_Type_Id is an IDENTITY
-- column, so it is not supplied.
--
-- Idempotent: guarded by NOT EXISTS on Request_Type_Code. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_029_SeedAutoFlagRequestType.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0005')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0005', N'Auto Flag Modification Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0005 - Auto Flag Modification Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0005 already exists - skipped.';
GO

PRINT '2026_07_029 complete - Auto Flag request type (0005) seeded.';
GO
