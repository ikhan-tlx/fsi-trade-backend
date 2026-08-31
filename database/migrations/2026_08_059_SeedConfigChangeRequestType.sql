-- =========================================================================
-- 2026_08_059_SeedConfigChangeRequestType.sql
-- -------------------------------------------------------------------------
-- Maker/checker unification — Configuration Management.
--
-- A configuration setting flagged Requires_Approval=1 (e.g. Access token
-- lifetime) must not change the effective value directly. Saving one now parks
-- the proposed value (TmX_Configuration_Value.Pending_Value) AND files a
-- pending TmX_Request (Request_Type_Code = '0011') so the change is visible and
-- actionable in the Requests screen (same pattern as User / Role / Branch /
-- Auto Flag / Manual Red Flag / Field Config / Reference Lists).
--
-- The generic Submit endpoint writes the Request row; Request_Type_Code
-- FK-references dbo.TmX_Request_Type, so the lookup row must exist.
--
-- On approval the checker's decision replays ApplyConfigChangeCommand through
-- its real handler — it sets the effective value and clears Pending_Value
-- (bypassing the Requires_Approval "park" branch so it does not re-queue).
-- Reject clears Pending_Value; the effective value was never touched.
--
-- Request_Type_Id is an IDENTITY column, so it is not supplied.
-- Idempotent: guarded by NOT EXISTS on Request_Type_Code. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_08_059_SeedConfigChangeRequestType.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0011')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0011', N'Configuration Change Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0011 - Configuration Change Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0011 already exists - skipped.';
GO

PRINT '2026_08_059 complete - Configuration Change request type (0011) seeded.';
GO
