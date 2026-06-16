/*============================================================================
  Migration:  2026_05_003_MovePasswordChangeAuditTrail.sql
  Purpose:    1) Create [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail]
                 (target for the new .NET 8 backend).
              2) IF [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail]
                 exists, move its rows into the new target, re-keying
                 User_Id (-> AspNetUsers.Id) to TmX_User.User_ID via the same
                 UserName join used in 2026_05_001.

              If the source table doesn't exist (legacy code-first migration
              never deployed it), we just create an empty target. The new
              backend will populate it from this point forward.

  How the FK type is decided:
              The script reads the real data type of [TmX_User].[User_ID]
              from sys.columns and builds the new User_ID column to match
              exactly.

  Idempotent: yes -- IF NOT EXISTS guards + UNIQUE on Source_Audit_Trail_ID.

  Usage:      Run with @DryRun = 1 first to inspect counts; flip to 0 to commit.
              If source is missing, target is still created either way.

  Author:     planning step 1, ICBC Backend Modernisation programme
============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

----------------------------------------------------------------------
-- CONFIG
----------------------------------------------------------------------
DECLARE @DryRun         BIT          = 1;
DECLARE @MigrationActor VARCHAR(40)  = 'AUDIT_MOVE_2026_05_003';
DECLARE @SeparatorBig   VARCHAR(80)  = REPLICATE('=', 70);
DECLARE @SeparatorRow   VARCHAR(80)  = REPLICATE('-', 70);

PRINT @SeparatorBig;
PRINT '  Migration: 2026_05_003_MovePasswordChangeAuditTrail';
PRINT '  Mode:      ' + CASE WHEN @DryRun = 1
                             THEN 'DRY RUN -- transaction will be ROLLED BACK'
                             ELSE 'COMMIT -- changes will persist' END;
PRINT '  Started:   ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;

----------------------------------------------------------------------
-- STAGE 0 -- PREFLIGHT
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 0 -- Preflight';
PRINT @SeparatorRow;

IF DB_ID('ICBC_DEMO_AUTH') IS NULL THROW 50001, '[ICBC_DEMO_AUTH] not found.', 1;
IF DB_ID('ICBC_DEMO')      IS NULL THROW 50002, '[ICBC_DEMO] not found.', 1;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
               WHERE name = 'TmX_User' AND SCHEMA_NAME(schema_id) = 'dbo')
    THROW 50003, '[ICBC_DEMO].[dbo].[TmX_User] not found. Run 2026_05_001 first.', 1;

PRINT '  [ok] Both databases present';
PRINT '  [ok] [ICBC_DEMO].[dbo].[TmX_User] present';

DECLARE @SourceExists BIT = 0;

IF EXISTS (SELECT 1 FROM [ICBC_DEMO_AUTH].sys.tables
           WHERE name = 'TMX_Password_Change_Audit_Trail' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    SET @SourceExists = 1;
    PRINT '  [ok] Source [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail] present';
END
ELSE
BEGIN
    PRINT '  [info] Source [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail] NOT found.';
    PRINT '         Target table will still be created so the new backend can populate it.';
    PRINT '';
    PRINT '         Tables in [ICBC_DEMO_AUTH] that match %Password% or %Audit%:';

    SELECT name AS Possible_Match_In_Auth_DB
    FROM   [ICBC_DEMO_AUTH].sys.tables
    WHERE  name LIKE '%Password%' OR name LIKE '%Audit%'
    ORDER  BY name;
END;

----------------------------------------------------------------------
-- STAGE 1 -- DETECT TmX_User.User_ID TYPE, CREATE TARGET TABLE
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 1 -- Create target [Password_Change_Audit_Trail]';
PRINT @SeparatorRow;

DECLARE @UserIdTypeDef NVARCHAR(100);

SELECT @UserIdTypeDef =
    ty.name +
    CASE
        WHEN c.max_length = -1                                THEN '(MAX)'
        WHEN ty.name IN ('nvarchar', 'nchar')                 THEN '(' + CAST(c.max_length / 2 AS VARCHAR(10)) + ')'
        WHEN ty.name IN ('varchar', 'char', 'binary',
                         'varbinary')                         THEN '(' + CAST(c.max_length AS VARCHAR(10)) + ')'
        ELSE ''
    END
FROM   [ICBC_DEMO].sys.columns c
JOIN   [ICBC_DEMO].sys.types   ty ON ty.user_type_id = c.user_type_id
                                 AND ty.is_user_defined = 0
WHERE  c.object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]')
  AND  c.name      = N'User_ID';

IF @UserIdTypeDef IS NULL
    THROW 50006, 'Could not detect [TmX_User].[User_ID] data type via sys.columns.', 1;

PRINT '  [info] Detected TmX_User.User_ID type: ' + @UserIdTypeDef;

IF EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
           WHERE name = 'Password_Change_Audit_Trail' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    PRINT '  [ok] [Password_Change_Audit_Trail] already exists';
END
ELSE
BEGIN
    DECLARE @CreateSql NVARCHAR(MAX) = N'
CREATE TABLE [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail] (
    Audit_Trail_ID            BIGINT IDENTITY(1,1) NOT NULL,
    User_ID                   ' + @UserIdTypeDef + N' NOT NULL,
    Password_Hash             NVARCHAR(MAX) NULL,
    Created_By                VARCHAR(40)   NULL,
    Created_Date              DATETIME      NOT NULL CONSTRAINT DF_Password_Change_Audit_Trail_CreatedDate DEFAULT (GETUTCDATE()),
    Source_Audit_Trail_ID     INT           NULL,
    Migrated_From_AspNet_Id   NVARCHAR(450) NULL,

    CONSTRAINT PK_Password_Change_Audit_Trail
        PRIMARY KEY (Audit_Trail_ID),

    CONSTRAINT UQ_Password_Change_Audit_Trail_SourceId
        UNIQUE (Source_Audit_Trail_ID),

    CONSTRAINT FK_Password_Change_Audit_Trail_TmX_User
        FOREIGN KEY (User_ID)
        REFERENCES  [ICBC_DEMO].[dbo].[TmX_User] (User_ID)
);

CREATE INDEX IX_Password_Change_Audit_Trail_User_ID
    ON [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail] (User_ID, Created_Date DESC);
';

    EXEC sp_executesql @CreateSql;

    PRINT '  [add] Created [Password_Change_Audit_Trail] (User_ID typed as ' + @UserIdTypeDef + ')';
    PRINT '         + PK / UQ / FK / 1 index';
END;

----------------------------------------------------------------------
-- STAGE 2 -- DATA COPY (only if source exists)
----------------------------------------------------------------------
IF @SourceExists = 0
BEGIN
    PRINT '';
    PRINT 'STAGE 2 -- Skipped (source table does not exist)';
    PRINT @SeparatorRow;
    PRINT '  No data to migrate. Target table is ready for the new backend to populate.';
    PRINT '';
    PRINT @SeparatorBig;
    PRINT '  Finished:  ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
    PRINT @SeparatorBig;
    RETURN;
END;

----------------------------------------------------------------------
-- STAGE 2 -- COUNTS
----------------------------------------------------------------------
DECLARE @SourceCount      INT,
        @AlreadyMigrated  INT,
        @ResolvableCount  INT,
        @OrphanedCount    INT;

SELECT @SourceCount = COUNT(*)
FROM   [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail];

SELECT @AlreadyMigrated = COUNT(*)
FROM   [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail]
WHERE  Source_Audit_Trail_ID IS NOT NULL;

SELECT @ResolvableCount = COUNT(*)
FROM   [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail] s
INNER  JOIN [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
        ON  a.Id = s.User_Id
INNER  JOIN [ICBC_DEMO].[dbo].[TmX_User] t
        ON  UPPER(t.User_Name) = UPPER(a.UserName)
WHERE  NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail] d
                   WHERE  d.Source_Audit_Trail_ID = s.Audit_Trail_ID);

SELECT @OrphanedCount = COUNT(*)
FROM   [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail] s
WHERE  NOT EXISTS (
           SELECT 1
           FROM   [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
           INNER  JOIN [ICBC_DEMO].[dbo].[TmX_User]    t
                  ON   UPPER(t.User_Name) = UPPER(a.UserName)
           WHERE  a.Id = s.User_Id);

PRINT '';
PRINT 'STAGE 2 -- Counts';
PRINT @SeparatorRow;
PRINT '  Source rows in Auth DB:                ' + CAST(@SourceCount     AS VARCHAR(10));
PRINT '  Already migrated to Finance DB:        ' + CAST(@AlreadyMigrated AS VARCHAR(10));
PRINT '  Resolvable & not yet migrated:         ' + CAST(@ResolvableCount AS VARCHAR(10));
PRINT '  Orphaned (no resolvable TmX_User):     ' + CAST(@OrphanedCount   AS VARCHAR(10));

----------------------------------------------------------------------
-- STAGE 3 -- COPY (transactional)
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 3 -- Copy resolvable rows';
PRINT @SeparatorRow;

DECLARE @CopiedCount      INT = 0;
DECLARE @ValidationFailed BIT = 0;

BEGIN TRANSACTION CopyAuditTrail;

INSERT INTO [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail] (
    User_ID,
    Password_Hash,
    Created_By,
    Created_Date,
    Source_Audit_Trail_ID,
    Migrated_From_AspNet_Id
)
SELECT  t.User_ID,
        s.Password_Hash,
        ISNULL(s.Created_By, @MigrationActor),
        s.Created_Date,
        s.Audit_Trail_ID,
        s.User_Id
FROM    [ICBC_DEMO_AUTH].[dbo].[TMX_Password_Change_Audit_Trail] s
INNER   JOIN [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
        ON   a.Id = s.User_Id
INNER   JOIN [ICBC_DEMO].[dbo].[TmX_User]         t
        ON   UPPER(t.User_Name) = UPPER(a.UserName)
WHERE   NOT EXISTS (SELECT 1
                    FROM   [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail] d
                    WHERE  d.Source_Audit_Trail_ID = s.Audit_Trail_ID);

SET @CopiedCount = @@ROWCOUNT;

PRINT '  [ins] Copied ' + CAST(@CopiedCount AS VARCHAR(10)) + ' row(s)';

----------------------------------------------------------------------
-- STAGE 4 -- VALIDATION
----------------------------------------------------------------------
DECLARE @TargetCountAfter INT;
SELECT  @TargetCountAfter = COUNT(*)
FROM    [ICBC_DEMO].[dbo].[Password_Change_Audit_Trail]
WHERE   Source_Audit_Trail_ID IS NOT NULL;

DECLARE @ExpectedAfter INT = @AlreadyMigrated + @CopiedCount;

PRINT '';
PRINT 'STAGE 4 -- Validation';
PRINT @SeparatorRow;
PRINT '  Migrated rows in target after run:     ' + CAST(@TargetCountAfter AS VARCHAR(10));
PRINT '  Expected (AlreadyMigrated + Copied):   ' + CAST(@ExpectedAfter    AS VARCHAR(10));

IF @TargetCountAfter <> @ExpectedAfter
BEGIN
    PRINT '  [fail] Validation failed -- counts diverge';
    SET @ValidationFailed = 1;
END
ELSE
    PRINT '  [ok] Validation passed';

----------------------------------------------------------------------
-- STAGE 5 -- COMMIT OR ROLLBACK
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 5 -- Transaction outcome';
PRINT @SeparatorRow;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION CopyAuditTrail;
    PRINT '  DRY RUN -- transaction ROLLED BACK. Target table additions remain.';
    PRINT '  Re-run with @DryRun = 0 to commit data.';
END
ELSE IF @ValidationFailed = 1
BEGIN
    ROLLBACK TRANSACTION CopyAuditTrail;
    PRINT '  VALIDATION FAILED -- transaction ROLLED BACK.';
END
ELSE
BEGIN
    COMMIT TRANSACTION CopyAuditTrail;
    PRINT '  COMMITTED.';
    IF @OrphanedCount > 0
    BEGIN
        PRINT '';
        PRINT '  [warn] ' + CAST(@OrphanedCount AS VARCHAR(10))
              + ' source row(s) had no resolvable TmX_User and were not migrated.';
    END;
END;

PRINT '';
PRINT @SeparatorBig;
PRINT '  Finished:  ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;
