-- =========================================================================
-- 2026_05_006_LookupConfigInventory.sql — read-only Slice 3 schema check.
-- Run on ICBC_DEMO. Returns column lists + row counts + sample rows for
-- TmX_Lookup and TmX_Configuration so the new Domain entities map exactly.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

PRINT '== TmX_Lookup — column shapes ==';
SELECT  ORDINAL_POSITION,
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_NAME = 'TmX_Lookup'
ORDER   BY ORDINAL_POSITION;

PRINT '== TmX_Lookup — row count + first 5 rows ==';
SELECT COUNT(*) AS [Total_Rows] FROM dbo.TmX_Lookup;
SELECT TOP 5 * FROM dbo.TmX_Lookup ORDER BY Lookup_ID;

PRINT '';
PRINT '== TmX_Configuration — column shapes ==';
SELECT  ORDINAL_POSITION,
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_NAME = 'TmX_Configuration'
ORDER   BY ORDINAL_POSITION;

PRINT '== TmX_Configuration — row count + first 5 rows ==';
SELECT COUNT(*) AS [Total_Rows] FROM dbo.TmX_Configuration;
SELECT TOP 5 * FROM dbo.TmX_Configuration ORDER BY Configuration_ID;

PRINT '';
PRINT '== Locale tables — to map culture strings to Locale_ID ==';
-- The legacy backend's sp_GetLookupByCulture takes 'en' / 'ar' / etc.
-- and resolves to a Locale_ID from somewhere. Find that table.
SELECT  TABLE_NAME
FROM    INFORMATION_SCHEMA.TABLES
WHERE   TABLE_NAME LIKE '%Locale%'
   OR   TABLE_NAME LIKE '%Culture%'
ORDER   BY TABLE_NAME;
GO
