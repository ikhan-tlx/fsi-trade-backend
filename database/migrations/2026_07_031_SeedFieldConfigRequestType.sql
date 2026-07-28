-- =========================================================================
-- 2026_07_031_SeedFieldConfigRequestType.sql
-- -------------------------------------------------------------------------
-- Maker/checker unification — Field Configuration.
--
-- Field Configuration edits the LIVE form schema (labels, placeholders,
-- visibility, required, field order and safe type changes per section) and is
-- critical to how every transaction/KYC form renders and validates. The
-- admin's "Review & publish" now parks the batch as a pending TmX_Request
-- (request-type 0007) instead of committing directly, so every form-schema
-- change is checker-approved (same pattern as User / Role / Branch / Auto Flag
-- / Manual Red Flag). The generic Submit endpoint writes a Request row with
-- Request_Type_Code = '0007', which FK-references dbo.TmX_Request_Type, so the
-- lookup row must exist (mirrors 2026_07_029 / _030).
--
-- On approval the checker's decision replays PublishSectionCommand through its
-- real handler (all its guardrails: safe type-change matrix, conditional-rule
-- protection, sequence re-write). Reject is a no-op — the live form schema is
-- never touched until approval.
--
-- Request_Type_Id is an IDENTITY column, so it is not supplied.
-- Idempotent: guarded by NOT EXISTS on Request_Type_Code. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_031_SeedFieldConfigRequestType.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Request_Type WHERE Request_Type_Code = N'0007')
BEGIN
    INSERT INTO dbo.TmX_Request_Type (Request_Type_Code, Request_Type_Dsc, Is_Active, User_Editable)
    VALUES (N'0007', N'Field Configuration Change Request', 1, 0);
    PRINT 'Seeded TmX_Request_Type 0007 - Field Configuration Change Request.';
END
ELSE
    PRINT 'TmX_Request_Type 0007 already exists - skipped.';
GO

PRINT '2026_07_031 complete - Field Configuration request type (0007) seeded.';
GO
