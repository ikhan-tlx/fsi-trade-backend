-- =========================================================================
-- 2026_05_008_WorkflowSchemaInventory.sql — read-only schema check.
-- Verifies v21 column names on the four Workflow* tables our new backend
-- reads from. Run BEFORE applying the EF entity mappings so we know
-- whether to map `Code` or `SchemeCode`, etc.
--
-- Safe to run repeatedly. NO writes.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

PRINT '== WorkflowProcessScheme — column list ==';
SELECT  ORDINAL_POSITION,
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_NAME = 'WorkflowProcessScheme'
ORDER   BY ORDINAL_POSITION;

PRINT '';
PRINT '== WorkflowProcessInstance — column list ==';
SELECT  ORDINAL_POSITION,
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_NAME = 'WorkflowProcessInstance'
ORDER   BY ORDINAL_POSITION;

PRINT '';
PRINT '== WorkflowInbox — column list ==';
SELECT  ORDINAL_POSITION,
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_NAME = 'WorkflowInbox'
ORDER   BY ORDINAL_POSITION;

PRINT '';
PRINT '== WorkflowProcessTransitionHistory — column list (sanity check) ==';
SELECT  ORDINAL_POSITION,
        COLUMN_NAME,
        DATA_TYPE
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_NAME = 'WorkflowProcessTransitionHistory'
ORDER   BY ORDINAL_POSITION;

PRINT '';
PRINT '== Sample row counts ==';
SELECT  'WorkflowProcessScheme'  AS [Table], COUNT(*) AS [Rows] FROM dbo.WorkflowProcessScheme
UNION ALL
SELECT  'WorkflowProcessInstance', COUNT(*) FROM dbo.WorkflowProcessInstance
UNION ALL
SELECT  'WorkflowInbox',           COUNT(*) FROM dbo.WorkflowInbox;
GO
