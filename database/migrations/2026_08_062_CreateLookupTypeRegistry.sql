-- =========================================================================
-- 2026_08_062_CreateLookupTypeRegistry.sql
-- -------------------------------------------------------------------------
-- Registry of admin-manageable lookup TYPES (TmX_Lookup_Type). A lookup "type"
-- is otherwise only a distinct Lookup_Type string across TmX_Lookup rows; this
-- table records which types may be managed from Lookup Management so that
-- NEWLY CREATED types are governed as data (auto-allow on create) rather than a
-- source-compiled allow-list.
--
-- The legacy compiled allow-list (LookupAdminAllowList.Types) still covers the
-- original curated types; the manageability guard now treats a type as
-- manageable if it is on that compiled list OR has a row here with
-- Is_Manageable = 1. New types created from the UI insert a row here
-- (Is_Manageable = 1, Is_System = 0) on approval.
--
-- Idempotent. Run on: ICBC_DEMO
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;

IF OBJECT_ID('dbo.TmX_Lookup_Type','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Lookup_Type (
        Lookup_Type_Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Lookup_Type PRIMARY KEY,
        Lookup_Type    NVARCHAR(100) NOT NULL,
        Is_Manageable  BIT           NOT NULL CONSTRAINT DF_TmX_Lookup_Type_Manage  DEFAULT (1),
        Is_System      BIT           NOT NULL CONSTRAINT DF_TmX_Lookup_Type_System  DEFAULT (0),
        Created_By     NVARCHAR(200) NULL,
        Created_Date   DATETIME2(3)  NOT NULL CONSTRAINT DF_TmX_Lookup_Type_Created DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_TmX_Lookup_Type_Type ON dbo.TmX_Lookup_Type(Lookup_Type);
    PRINT 'Created dbo.TmX_Lookup_Type';
END
ELSE PRINT 'TmX_Lookup_Type already exists - no-op';
GO

PRINT '2026_08_062 complete - lookup type registry created.';
GO
