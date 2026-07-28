-- =========================================================================
-- 2026_07_030_SeedManualFlagRequestType.sql
-- -------------------------------------------------------------------------
-- Maker/checker unification — Manual Red Flags.
--
-- The Manual Red Flag admin screen now parks its Create / Update / Retire as a
-- pending TmX_Request (request-type 0006) instead of committing directly, so
-- every manual-flag definition change is checker-approved (same pattern as
-- User / Role / Branch). The generic Submit endpoint writes a Request row with
-- Request_Type_Code = '0006'; that FK-references dbo.TmX_Request_Type, so the
-- lookup row must exist (mirrors 2026_07_029 for the Auto Flag type 0005).
--
-- Request_Type_Id is an IDENTITY column, so it is not supplied.
--
-- Idempotent: guarded by NOT EXISTS on Request_Type_Code. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_030_SeedManualFlagRequestType.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0006')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0006', N'Manual Red Flag Modification Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0006 - Manual Red Flag Modification Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0006 already exists - skipped.';
GO

PRINT '2026_07_030 complete - Manual Red Flag request type (0006) seeded.';
GO
