-- =========================================================================
-- RBAC schema inventory — read-only diagnostic for Slice 2 (User & RBAC).
--
-- Purpose:
--   Confirm column shapes, exact table names, FK type alignment, and a
--   sample of actual privilege strings before we write EF Core entities
--   in the new FSI.Trade.Compliance backend.
--
-- Safe to run repeatedly. Performs NO writes.
-- =========================================================================

USE [ICBC_DEMO];
GO

PRINT '';
PRINT '== Step 1 — confirm table names actually present ==';
SELECT  s.name + '.' + t.name AS [Table]
FROM    sys.tables t
JOIN    sys.schemas s ON s.schema_id = t.schema_id
WHERE   t.name IN ('TmX_Role',
                   'TmX_Privilege',
                   'TmX_Privileges',
                   'TmX_Role_Privilege_Mapping',
                   'TmX_Role_Privilege_Mappings',
                   'TmX_User_Role_Mapping',
                   'TmX_User_Role_Mappings')
ORDER   BY t.name;
GO

PRINT '';
PRINT '== Step 2 — column shapes for the four RBAC tables ==';

DECLARE @tables TABLE (name sysname);
INSERT  @tables (name) VALUES
    ('TmX_Role'),
    ('TmX_Privilege'),
    ('TmX_Privileges'),
    ('TmX_Role_Privilege_Mapping'),
    ('TmX_Role_Privilege_Mappings'),
    ('TmX_User_Role_Mapping'),
    ('TmX_User_Role_Mappings');

SELECT  c.TABLE_NAME,
        c.ORDINAL_POSITION,
        c.COLUMN_NAME,
        c.DATA_TYPE,
        c.CHARACTER_MAXIMUM_LENGTH,
        c.IS_NULLABLE,
        c.COLUMN_DEFAULT
FROM    INFORMATION_SCHEMA.COLUMNS c
WHERE   c.TABLE_NAME IN (SELECT name FROM @tables)
ORDER   BY c.TABLE_NAME, c.ORDINAL_POSITION;
GO

PRINT '';
PRINT '== Step 3 — primary keys ==';
SELECT  t.name              AS [Table],
        c.name              AS [PK_Column],
        ic.key_ordinal      AS [Position]
FROM    sys.indexes i
JOIN    sys.tables  t  ON t.object_id = i.object_id
JOIN    sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN    sys.columns c  ON c.object_id = i.object_id AND c.column_id = ic.column_id
WHERE   i.is_primary_key = 1
  AND   t.name LIKE 'TmX_%'
  AND   t.name IN ('TmX_Role',
                   'TmX_Privilege', 'TmX_Privileges',
                   'TmX_Role_Privilege_Mapping', 'TmX_Role_Privilege_Mappings',
                   'TmX_User_Role_Mapping', 'TmX_User_Role_Mappings')
ORDER   BY t.name, ic.key_ordinal;
GO

PRINT '';
PRINT '== Step 4 — foreign keys ==';
SELECT  fk.name                         AS [FK_Name],
        OBJECT_NAME(fk.parent_object_id) AS [Parent_Table],
        cp.name                         AS [Parent_Column],
        OBJECT_NAME(fk.referenced_object_id) AS [Referenced_Table],
        cr.name                         AS [Referenced_Column]
FROM    sys.foreign_keys fk
JOIN    sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN    sys.columns cp ON cp.object_id = fkc.parent_object_id     AND cp.column_id = fkc.parent_column_id
JOIN    sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
WHERE   OBJECT_NAME(fk.parent_object_id) IN ('TmX_Role',
                                             'TmX_Privilege', 'TmX_Privileges',
                                             'TmX_Role_Privilege_Mapping', 'TmX_Role_Privilege_Mappings',
                                             'TmX_User_Role_Mapping', 'TmX_User_Role_Mappings')
ORDER   BY OBJECT_NAME(fk.parent_object_id), fk.name;
GO

PRINT '';
PRINT '== Step 5 — sample privilege rows (top 25 by name) ==';
-- Try both possible singular/plural names. Whichever exists, returns rows.
IF OBJECT_ID('dbo.TmX_Privileges',N'U') IS NOT NULL
BEGIN
    PRINT '   (table is plural: TmX_Privileges)';
    EXEC sp_executesql N'SELECT TOP 25 * FROM dbo.TmX_Privileges ORDER BY 1';
END
ELSE IF OBJECT_ID('dbo.TmX_Privilege',N'U') IS NOT NULL
BEGIN
    PRINT '   (table is singular: TmX_Privilege)';
    EXEC sp_executesql N'SELECT TOP 25 * FROM dbo.TmX_Privilege ORDER BY 1';
END
ELSE
BEGIN
    PRINT '   !! No TmX_Privilege[s] table found on this database.';
END
GO

PRINT '';
PRINT '== Step 6 — sample role rows (top 25) + total row counts ==';
SELECT TOP 25 * FROM dbo.TmX_Role ORDER BY 1;

SELECT  'TmX_Role'                       AS [Table], COUNT(*) AS [Rows] FROM dbo.TmX_Role
UNION ALL
SELECT  'TmX_User_Role_Mapping',  COUNT(*) FROM dbo.TmX_User_Role_Mapping;
GO

PRINT '';
PRINT '== Step 7 — sample role-privilege mappings (top 25) ==';
IF OBJECT_ID('dbo.TmX_Role_Privilege_Mappings',N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT TOP 25 * FROM dbo.TmX_Role_Privilege_Mappings ORDER BY 1';
ELSE IF OBJECT_ID('dbo.TmX_Role_Privilege_Mapping',N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT TOP 25 * FROM dbo.TmX_Role_Privilege_Mapping ORDER BY 1';
ELSE
    PRINT '   !! No TmX_Role_Privilege_Mapping[s] table found.';
GO

PRINT '';
PRINT '== Step 8 — User_Id type alignment (TmX_User vs TmX_User_Role_Mapping) ==';
-- Confirms the FK column type matches the parent. If they differ we'll fail
-- at FK creation in any future migration that adds an explicit constraint.
SELECT  t.name AS [Table],
        c.name AS [Column],
        ty.name AS [Type],
        c.max_length,
        c.is_nullable
FROM    sys.columns c
JOIN    sys.tables  t  ON t.object_id = c.object_id
JOIN    sys.types   ty ON ty.user_type_id = c.user_type_id
WHERE   (t.name = 'TmX_User'                AND c.name IN ('User_ID',  'User_Id'))
   OR   (t.name = 'TmX_User_Role_Mapping'   AND c.name IN ('User_ID',  'User_Id', 'TmX_User_ID'))
ORDER   BY t.name, c.name;
GO

PRINT '';
PRINT '== Step 9 — DISTINCT privilege strings actually wired to roles ==';
-- Definitive list of every privilege string the legacy app ever checks
-- against. This is what [RequiresPrivilege("...")] will receive.
IF OBJECT_ID('dbo.TmX_Role_Privilege_Mappings',N'U') IS NOT NULL
   AND OBJECT_ID('dbo.TmX_Privileges',N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        SELECT  DISTINCT p.Privilege_Name, p.Privilege_Description
        FROM    dbo.TmX_Privileges p
        JOIN    dbo.TmX_Role_Privilege_Mappings rpm
               ON rpm.Privilege_ID = p.Privilege_ID
        ORDER   BY p.Privilege_Name;';
END
ELSE IF OBJECT_ID('dbo.TmX_Role_Privilege_Mapping',N'U') IS NOT NULL
     AND OBJECT_ID('dbo.TmX_Privilege',N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        SELECT  DISTINCT p.Privilege_Name, p.Privilege_Description
        FROM    dbo.TmX_Privilege p
        JOIN    dbo.TmX_Role_Privilege_Mapping rpm
               ON rpm.Privilege_ID = p.Privilege_ID
        ORDER   BY p.Privilege_Name;';
END
GO

PRINT '';
PRINT '== Inventory complete. ==';
