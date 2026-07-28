-- =========================================================================
-- 2026_07_032_SeedReferenceListRequestTypes.sql
-- -------------------------------------------------------------------------
-- Maker/checker unification — Reference Lists.
--
-- Reference Lists feed the Auto Flag rule engine (sanctioned countries, PEP
-- sets, watch-lists, etc.), so every change to a reference TYPE, a reference
-- VALUE, or a bulk value import is now checker-approved instead of committing
-- directly (same pattern as User / Role / Branch / Auto Flag / Manual Red Flag
-- / Field Configuration). The generic Submit endpoint writes a TmX_Request row
-- whose Request_Type_Code FK-references dbo.TmX_Request_Type, so the lookup
-- rows must exist (mirrors 2026_07_029 / _030 / _031).
--
-- One request type per sub-entity so the replay registry's (type, action)
-- switch can pick the exact command (types, values and bulk otherwise share
-- action codes 0001/0002/0003):
--   0008  Reference Type   — create / update / delete
--   0009  Reference Value  — create / update / delete
--   0010  Reference Bulk   — bulk value import into a type (action Update)
--
-- On approval the checker's decision replays the maker's original command
-- (SaveReferenceTypeCommand / DeleteReferenceTypeCommand /
--  SaveReferenceValueCommand / DeleteReferenceValueCommand /
--  BulkUpsertReferenceValuesCommand) through its real handler — all its
-- validation + persistence. Reject is a no-op — the live reference tables are
-- never touched until approval.
--
-- Request_Type_Id is an IDENTITY column, so it is not supplied.
-- Idempotent: guarded by NOT EXISTS on Request_Type_Code. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_032_SeedReferenceListRequestTypes.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0008')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0008', N'Reference Type Change Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0008 - Reference Type Change Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0008 already exists - skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0009')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0009', N'Reference Value Change Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0009 - Reference Value Change Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0009 already exists - skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0010')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0010', N'Reference List Bulk Import Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0010 - Reference List Bulk Import Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0010 already exists - skipped.';
GO

PRINT '2026_07_032 complete - Reference List request types (0008/0009/0010) seeded.';
GO
