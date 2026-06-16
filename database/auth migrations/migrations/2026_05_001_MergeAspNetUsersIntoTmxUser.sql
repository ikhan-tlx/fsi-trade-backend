/*============================================================================
  Migration:  2026_05_001_MergeAspNetUsersIntoTmxUser.sql
  Purpose:    Additive merge of [ICBC_DEMO_AUTH].[dbo].[AspNetUsers]
              into [ICBC_DEMO].[dbo].[TmX_User].
              - Adds 13 auth columns to TmX_User
              - Copies values from AspNetUsers for matched rows
              - Creates stub TmX_User rows for orphan auth users
              - Logs orphan business users (no credentials -> forced reset)

  IMPORTANT:  This script is ADDITIVE ONLY. It does NOT drop AspNet* tables.
              The drop is handled by 2026_05_002_DropAspNetTables.sql at
              T+30 days post-cutover.

  Decisions baked in:
    - Match key:        UPPER(UserName) = UPPER(User_Name)
    - Orphan auth-side: Create stub TmX_User row with Active_Flag = 0,
                        log to TmX_User_Reconciliation_Log
    - Orphan tmx-side:  Leave PasswordHash NULL, log to reconciliation table
    - Conflict rule:    Prefer existing TmX_User value if non-empty
    - Status / Active:  Keep both, derive Status from Active_Flag where NULL
    - Same SQL Server:  Yes -- 4-part names

  Why is half the script wrapped in EXEC sp_executesql?
    SQL Server compiles a whole batch before executing any of it. The
    UPDATE/INSERT statements reference columns added by the ALTER TABLE
    statements above them in the same batch. At compile time those columns
    don't exist yet -> "Invalid column name 'PasswordHash'". Splitting with
    GO would solve it but kill our variables and transaction. Putting the
    DML in dynamic SQL defers name resolution to execution time, which is
    after the ALTERs have run. Same single file, same one execution.

  Phases:
    PHASE 1 (static, no transaction): preflight + schema additions.
            Idempotent. Re-running is a no-op once columns exist.
    PHASE 2 (transaction, gated by @DryRun): all data changes via
            EXEC sp_executesql. ROLLBACK on dry-run, COMMIT on real run.

  Usage:
    1. Take a fresh full backup of [ICBC_DEMO_AUTH] AND [ICBC_DEMO].
    2. Restore to QA. Run with @DryRun = 1 (default). Review report.
    3. If report looks correct, set @DryRun = 0 and re-run.
    4. Repeat on production.

  Idempotent: yes. Safe to re-run any number of times in either mode.

  Author: planning step 1, ICBC Backend Modernisation programme
============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

----------------------------------------------------------------------
-- CONFIGURATION (review before COMMIT run)
----------------------------------------------------------------------
DECLARE @DryRun           BIT          = 1;        -- 1 = preview; 0 = persist
DECLARE @MigrationActor   VARCHAR(40)  = 'AUTH_MERGE_2026_05_001';
DECLARE @DefaultTenantId  INT          = 1;        -- Tenant_ID assigned to stub rows; verify
DECLARE @SeparatorRow     VARCHAR(80)  = REPLICATE('-', 70);
DECLARE @SeparatorBig     VARCHAR(80)  = REPLICATE('=', 70);

PRINT @SeparatorBig;
PRINT '  Migration:  AspNetUsers -> TmX_User (additive)';
PRINT '  Mode:       ' + CASE WHEN @DryRun = 1
                              THEN 'DRY RUN -- data changes will be ROLLED BACK'
                              ELSE 'COMMIT -- data changes will persist' END;
PRINT '  Note:       Schema changes (ALTER TABLE) are idempotent and';
PRINT '              committed regardless of @DryRun. Re-runs are no-ops.';
PRINT '  Actor:      ' + @MigrationActor;
PRINT '  Started:    ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;

----------------------------------------------------------------------
-- STAGE 0: PREFLIGHT
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 0 -- Preflight checks';
PRINT @SeparatorRow;

IF DB_ID('ICBC_DEMO_AUTH') IS NULL
    THROW 50001, 'Database [ICBC_DEMO_AUTH] not found on this instance.', 1;
IF DB_ID('ICBC_DEMO') IS NULL
    THROW 50002, 'Database [ICBC_DEMO] not found on this instance.', 1;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO_AUTH].sys.tables
               WHERE name = 'AspNetUsers' AND SCHEMA_NAME(schema_id) = 'dbo')
    THROW 50003, '[ICBC_DEMO_AUTH].[dbo].[AspNetUsers] not found.', 1;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
               WHERE name = 'TmX_User' AND SCHEMA_NAME(schema_id) = 'dbo')
    THROW 50004, '[ICBC_DEMO].[dbo].[TmX_User] not found.', 1;

PRINT '  [ok] Both databases present on this instance';
PRINT '  [ok] [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] exists';
PRINT '  [ok] [ICBC_DEMO].[dbo].[TmX_User] exists';

DECLARE @AuthCount       INT,
        @TmxCount        INT,
        @MatchedCount    INT,
        @OrphanAuthCount INT,
        @OrphanTmxCount  INT,
        @AuthDupes       INT,
        @TmxDupes        INT;

SELECT @AuthCount = COUNT(*) FROM [ICBC_DEMO_AUTH].[dbo].[AspNetUsers];
SELECT @TmxCount  = COUNT(*) FROM [ICBC_DEMO].[dbo].[TmX_User];

SELECT @MatchedCount = COUNT(*)
FROM   [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
INNER  JOIN [ICBC_DEMO].[dbo].[TmX_User]    t
       ON UPPER(a.UserName) = UPPER(t.User_Name);

SELECT @OrphanAuthCount = COUNT(*)
FROM   [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
WHERE  NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] t
                   WHERE  UPPER(t.User_Name) = UPPER(a.UserName));

SELECT @OrphanTmxCount = COUNT(*)
FROM   [ICBC_DEMO].[dbo].[TmX_User] t
WHERE  NOT EXISTS (SELECT 1 FROM [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
                   WHERE  UPPER(a.UserName) = UPPER(t.User_Name));

SELECT @AuthDupes = COUNT(*) FROM (
    SELECT UPPER(UserName) AS un
    FROM   [ICBC_DEMO_AUTH].[dbo].[AspNetUsers]
    GROUP  BY UPPER(UserName)
    HAVING COUNT(*) > 1
) dup;

SELECT @TmxDupes = COUNT(*) FROM (
    SELECT UPPER(User_Name) AS un
    FROM   [ICBC_DEMO].[dbo].[TmX_User]
    GROUP  BY UPPER(User_Name)
    HAVING COUNT(*) > 1
) dup;

PRINT '';
PRINT '  Counts:';
PRINT '    AspNetUsers (Auth DB):                 ' + CAST(@AuthCount       AS VARCHAR(10));
PRINT '    TmX_User    (Finance DB):              ' + CAST(@TmxCount        AS VARCHAR(10));
PRINT '    Matched (UPPER UserName join):         ' + CAST(@MatchedCount    AS VARCHAR(10));
PRINT '    Orphan in Auth   (will be stubbed):    ' + CAST(@OrphanAuthCount AS VARCHAR(10));
PRINT '    Orphan in TmX    (no credentials):     ' + CAST(@OrphanTmxCount  AS VARCHAR(10));
PRINT '';
PRINT '  Duplicates (case-insensitive UserName):';
PRINT '    in AspNetUsers:                        ' + CAST(@AuthDupes       AS VARCHAR(10));
PRINT '    in TmX_User:                           ' + CAST(@TmxDupes        AS VARCHAR(10));

IF @AuthDupes > 0 OR @TmxDupes > 0
BEGIN
    PRINT '';
    PRINT '  [warn] Username duplicates found. Sample below.';

    SELECT TOP 20 'AspNetUsers' AS Source, UPPER(UserName) AS UserName, COUNT(*) AS Cnt
    FROM   [ICBC_DEMO_AUTH].[dbo].[AspNetUsers]
    GROUP  BY UPPER(UserName) HAVING COUNT(*) > 1;

    SELECT TOP 20 'TmX_User'    AS Source, UPPER(User_Name) AS UserName, COUNT(*) AS Cnt
    FROM   [ICBC_DEMO].[dbo].[TmX_User]
    GROUP  BY UPPER(User_Name) HAVING COUNT(*) > 1;

    IF @DryRun = 0
        THROW 50010, 'Username duplicates exist on at least one side. Cannot COMMIT until resolved. Re-run with @DryRun = 1 to inspect.', 1;
    ELSE
        PRINT '  [warn] Continuing dry-run despite duplicates so the rest of the report can be reviewed.';
END;

----------------------------------------------------------------------
-- STAGE 1: SCHEMA CHANGES (idempotent, NOT transactional)
--
-- Adds 13 new columns. Each ADD is guarded by IF NOT EXISTS, so re-runs
-- are no-ops. These run regardless of @DryRun -- they are additive and
-- harmless without data.
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 1 -- Schema additions on [ICBC_DEMO].[dbo].[TmX_User]';
PRINT @SeparatorRow;

DECLARE @AddedCols TABLE (col_name SYSNAME);

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'PasswordHash')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD PasswordHash NVARCHAR(MAX) NULL;
    INSERT @AddedCols VALUES ('PasswordHash');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'SecurityStamp')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD SecurityStamp NVARCHAR(MAX) NULL;
    INSERT @AddedCols VALUES ('SecurityStamp');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'EmailConfirmed')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD EmailConfirmed BIT NOT NULL CONSTRAINT DF_TmX_User_EmailConfirmed DEFAULT (0);
    INSERT @AddedCols VALUES ('EmailConfirmed');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'PhoneNumberConfirmed')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD PhoneNumberConfirmed BIT NOT NULL CONSTRAINT DF_TmX_User_PhoneNumberConfirmed DEFAULT (0);
    INSERT @AddedCols VALUES ('PhoneNumberConfirmed');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'TwoFactorEnabled')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD TwoFactorEnabled BIT NOT NULL CONSTRAINT DF_TmX_User_TwoFactorEnabled DEFAULT (0);
    INSERT @AddedCols VALUES ('TwoFactorEnabled');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'TwoFASecretKey')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD TwoFASecretKey NVARCHAR(MAX) NULL;
    INSERT @AddedCols VALUES ('TwoFASecretKey');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'LockoutEndDateUtc')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD LockoutEndDateUtc DATETIME NULL;
    INSERT @AddedCols VALUES ('LockoutEndDateUtc');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'LockoutEnabled')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD LockoutEnabled BIT NOT NULL CONSTRAINT DF_TmX_User_LockoutEnabled DEFAULT (1);
    INSERT @AddedCols VALUES ('LockoutEnabled');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'AccessFailedCount')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD AccessFailedCount INT NOT NULL CONSTRAINT DF_TmX_User_AccessFailedCount DEFAULT (0);
    INSERT @AddedCols VALUES ('AccessFailedCount');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'Status')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD [Status] NVARCHAR(50) NULL;
    INSERT @AddedCols VALUES ('Status');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'LastLoginDate')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD LastLoginDate DATETIME NULL;
    INSERT @AddedCols VALUES ('LastLoginDate');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'PasswordExpiryDate')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD PasswordExpiryDate DATETIME NULL;
    INSERT @AddedCols VALUES ('PasswordExpiryDate');
END;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.columns
               WHERE  object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[TmX_User]') AND name = 'FirstPasswordChange')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[TmX_User] ADD FirstPasswordChange BIT NOT NULL CONSTRAINT DF_TmX_User_FirstPasswordChange DEFAULT (1);
    INSERT @AddedCols VALUES ('FirstPasswordChange');
END;

DECLARE @AddedCount INT = (SELECT COUNT(*) FROM @AddedCols);

IF @AddedCount = 0
    PRINT '  [ok] All 13 auth columns already exist on TmX_User -- no schema change required';
ELSE
BEGIN
    PRINT '  [add] Added ' + CAST(@AddedCount AS VARCHAR(5)) + ' new column(s) to TmX_User:';
    DECLARE @ColCsv NVARCHAR(2000) = '';
    SELECT @ColCsv = STRING_AGG(col_name, ', ') FROM @AddedCols;
    PRINT '         ' + @ColCsv;
END;

----------------------------------------------------------------------
-- STAGE 1b: RECONCILIATION LOG TABLE (idempotent)
----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
               WHERE name = 'TmX_User_Reconciliation_Log' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    CREATE TABLE [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log] (
        Log_ID         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TmX_User_Reconciliation_Log PRIMARY KEY,
        Migration_Code VARCHAR(40)   NOT NULL,
        Reason         VARCHAR(40)   NOT NULL,    -- 'OrphanAuthStubbed' | 'OrphanTmxNoCredentials' | 'CollisionSkipped'
        AspNet_Id      NVARCHAR(450) NULL,
        TmX_User_ID    VARCHAR(40)   NULL,
        UserName       NVARCHAR(256) NULL,
        Detail         NVARCHAR(MAX) NULL,
        Logged_At      DATETIME      NOT NULL CONSTRAINT DF_TmX_User_Recon_Log_LoggedAt DEFAULT (GETUTCDATE())
    );
    PRINT '  [add] Created [TmX_User_Reconciliation_Log] table';
END
ELSE
    PRINT '  [ok] [TmX_User_Reconciliation_Log] already exists';

----------------------------------------------------------------------
-- ============================  PHASE 2  ============================
-- Data changes inside a transaction. All DML is in dynamic SQL so the
-- newly-added columns are visible at execution time. Variables are passed
-- into sp_executesql; rowcounts come back via OUTPUT parameters.
----------------------------------------------------------------------
DECLARE @Sql              NVARCHAR(MAX);
DECLARE @Params           NVARCHAR(2000);
DECLARE @Stage2Updated    INT = 0;
DECLARE @Stage3Inserted   INT = 0;
DECLARE @Stage3Logged     INT = 0;
DECLARE @Stage4Logged     INT = 0;
DECLARE @Stage5Updated    INT = 0;
DECLARE @CollisionCount   INT = 0;
DECLARE @CollisionLogged  INT = 0;
DECLARE @TmxCountAfter    INT = 0;
DECLARE @TmxWithHash      INT = 0;
DECLARE @TmxNullHash      INT = 0;
DECLARE @TmxStatusNull    INT = 0;
DECLARE @ValidationFailed BIT = 0;

BEGIN TRANSACTION MergeAuthData;

----------------------------------------------------------------------
-- STAGE 2: BACKFILL MATCHED ROWS (dynamic SQL)
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 2 -- Backfill auth columns on matched TmX_User rows';
PRINT @SeparatorRow;

SET @Sql = N'
UPDATE  t
SET     t.PasswordHash         = a.PasswordHash,
        t.SecurityStamp        = a.SecurityStamp,
        t.EmailConfirmed       = ISNULL(a.EmailConfirmed, 0),
        t.PhoneNumberConfirmed = ISNULL(a.PhoneNumberConfirmed, 0),
        -- collapse the two 2FA flags: enabled if either source says so
        t.TwoFactorEnabled     = CASE
                                    WHEN ISNULL(a.TwoFactorEnabled, 0) = 1
                                      OR ISNULL(a.IsTwoFAEnabled,   0) = 1 THEN 1
                                    ELSE 0
                                 END,
        t.TwoFASecretKey       = a.TwoFASecretKey,
        t.LockoutEndDateUtc    = a.LockoutEndDateUtc,
        t.LockoutEnabled       = ISNULL(a.LockoutEnabled, 1),
        t.AccessFailedCount    = ISNULL(a.AccessFailedCount, 0),
        t.[Status]             = COALESCE(NULLIF(LTRIM(RTRIM(a.[Status])), N''''),
                                          CASE WHEN t.Active_Flag = 1 THEN N''Active'' ELSE N''InActive'' END),
        t.LastLoginDate        = a.LastLoginDate,
        t.PasswordExpiryDate   = a.PasswordExpiryDate,
        t.FirstPasswordChange  = ISNULL(a.FirstPasswordChange, 1),
        -- conflict resolution: prefer existing TmX value if non-empty, else AspNet
        t.Email_Address        = COALESCE(NULLIF(LTRIM(RTRIM(t.Email_Address)),  N''''), a.Email),
        t.Contact_Number       = COALESCE(NULLIF(LTRIM(RTRIM(t.Contact_Number)), N''''), a.PhoneNumber),
        t.First_Name           = COALESCE(NULLIF(LTRIM(RTRIM(t.First_Name)),     N''''), a.FirstName),
        t.Last_Name            = COALESCE(NULLIF(LTRIM(RTRIM(t.Last_Name)),      N''''), a.LastName),
        t.User_Image_URL       = COALESCE(NULLIF(LTRIM(RTRIM(t.User_Image_URL)), N''''), a.ImageURL),
        t.Last_Updated_Date    = GETUTCDATE(),
        t.Last_Updated_By      = @MigrationActor
FROM    [ICBC_DEMO].[dbo].[TmX_User]    AS t
INNER   JOIN [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] AS a
        ON UPPER(a.UserName) = UPPER(t.User_Name);
SELECT @rowcount = @@ROWCOUNT;
';

SET @Params = N'@MigrationActor VARCHAR(40), @rowcount INT OUTPUT';

EXEC sp_executesql @Sql, @Params,
     @MigrationActor = @MigrationActor,
     @rowcount       = @Stage2Updated OUTPUT;

PRINT '  [upd] Backfilled ' + CAST(@Stage2Updated AS VARCHAR(10)) + ' matched TmX_User row(s)';

IF @Stage2Updated <> @MatchedCount
    PRINT '  [warn] Updated count differs from preflight matched count -- investigate.';

----------------------------------------------------------------------
-- STAGE 3a: DETECT AND LOG USER_ID COLLISIONS (dynamic SQL not strictly
-- needed here, but keep parity with the rest of the data section)
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 3 -- Stub orphan AspNetUsers rows into TmX_User';
PRINT @SeparatorRow;

SET @Sql = N'
SELECT  @cnt = COUNT(*)
FROM    [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
WHERE   NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] tu
                    WHERE  UPPER(tu.User_Name) = UPPER(a.UserName))
  AND   EXISTS     (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] tu
                    WHERE  tu.User_ID = a.Id);';
EXEC sp_executesql @Sql, N'@cnt INT OUTPUT', @cnt = @CollisionCount OUTPUT;

IF @CollisionCount > 0
BEGIN
    PRINT '  [warn] ' + CAST(@CollisionCount AS VARCHAR(10)) + ' orphan AspNetUsers row(s) have an Id that collides with an existing TmX_User.User_ID. SKIPPED and logged.';

    SET @Sql = N'
INSERT INTO [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log]
    (Migration_Code, Reason, AspNet_Id, TmX_User_ID, UserName, Detail)
SELECT  @MigrationActor,
        ''CollisionSkipped'',
        a.Id,
        a.Id,
        a.UserName,
        ''AspNetUsers.Id matches an existing TmX_User.User_ID for a different user. Manual review required.''
FROM    [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
WHERE   NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] tu
                    WHERE  UPPER(tu.User_Name) = UPPER(a.UserName))
  AND   EXISTS     (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] tu
                    WHERE  tu.User_ID = a.Id)
  AND   NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log] L
                    WHERE  L.Migration_Code = @MigrationActor
                      AND  L.Reason         = ''CollisionSkipped''
                      AND  L.AspNet_Id      = a.Id);
SELECT @rowcount = @@ROWCOUNT;
';
    EXEC sp_executesql @Sql,
         N'@MigrationActor VARCHAR(40), @rowcount INT OUTPUT',
         @MigrationActor = @MigrationActor,
         @rowcount       = @CollisionLogged OUTPUT;
    PRINT '  [log] Logged ' + CAST(@CollisionLogged AS VARCHAR(10)) + ' collision(s)';
END;

----------------------------------------------------------------------
-- STAGE 3b: INSERT STUB ROWS FOR NON-COLLIDING ORPHAN AUTH USERS
--
-- User_ID for the stub uses AspNetUsers.Id verbatim (no external caching
-- per Decision 14). Active_Flag = 0; admin must complete profile.
----------------------------------------------------------------------
SET @Sql = N'
INSERT INTO [ICBC_DEMO].[dbo].[TmX_User] (
    User_ID, Parent_User_ID, Tenant_ID, Location_ID, Address_ID, Time_Zone_ID,
    User_Name, First_Name, Email_Address, Middle_Name, Last_Name, Contact_Number,
    User_Image_URL, Primary_National_Identifier_Value, Mobile_User_Flag, Active_Flag,
    Effective_Start_Date, Effective_End_Date,
    Created_By, Created_Date, Last_Updated_Date, Last_Updated_By,
    Primary_National_ID_Type_Lkp_ID, User_Type_Lkp_ID, Designation_Lkp_ID,
    Server_User_ID, Server_Branch_ID,
    PasswordHash, SecurityStamp, EmailConfirmed, PhoneNumberConfirmed,
    TwoFactorEnabled, TwoFASecretKey, LockoutEndDateUtc, LockoutEnabled,
    AccessFailedCount, [Status], LastLoginDate, PasswordExpiryDate, FirstPasswordChange
)
SELECT
    a.Id,
    NULL,
    @DefaultTenantId,
    NULL,
    NULL,
    NULL,
    a.UserName,
    ISNULL(a.FirstName, N''Unknown''),
    ISNULL(a.Email, N''''),
    N'''',
    ISNULL(a.LastName, N''User''),
    ISNULL(a.PhoneNumber, N''''),
    ISNULL(a.ImageURL, N''''),
    N'''',
    NULL,
    0,
    COALESCE(a.RegistrationDate, GETUTCDATE()),
    ''9999-12-31'',
    @MigrationActor,
    GETUTCDATE(),
    GETUTCDATE(),
    @MigrationActor,
    NULL,
    NULL,
    NULL,
    N'''',
    N'''',
    a.PasswordHash,
    a.SecurityStamp,
    ISNULL(a.EmailConfirmed, 0),
    ISNULL(a.PhoneNumberConfirmed, 0),
    CASE WHEN ISNULL(a.TwoFactorEnabled, 0) = 1
          OR ISNULL(a.IsTwoFAEnabled,   0) = 1 THEN 1 ELSE 0 END,
    a.TwoFASecretKey,
    a.LockoutEndDateUtc,
    ISNULL(a.LockoutEnabled, 1),
    ISNULL(a.AccessFailedCount, 0),
    COALESCE(NULLIF(LTRIM(RTRIM(a.[Status])), N''''), N''InActive''),
    a.LastLoginDate,
    a.PasswordExpiryDate,
    ISNULL(a.FirstPasswordChange, 1)
FROM    [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
WHERE   NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] t
                    WHERE  UPPER(t.User_Name) = UPPER(a.UserName))
  AND   NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User] tu
                    WHERE  tu.User_ID = a.Id);
SELECT @rowcount = @@ROWCOUNT;
';

EXEC sp_executesql @Sql,
     N'@MigrationActor VARCHAR(40), @DefaultTenantId INT, @rowcount INT OUTPUT',
     @MigrationActor   = @MigrationActor,
     @DefaultTenantId  = @DefaultTenantId,
     @rowcount         = @Stage3Inserted OUTPUT;

PRINT '  [ins] Stubbed ' + CAST(@Stage3Inserted AS VARCHAR(10)) + ' orphan AspNetUsers row(s) into TmX_User';

----------------------------------------------------------------------
-- STAGE 3c: LOG EACH STUB FOR ADMIN RECONCILIATION
----------------------------------------------------------------------
IF @Stage3Inserted > 0
BEGIN
    SET @Sql = N'
INSERT INTO [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log]
    (Migration_Code, Reason, AspNet_Id, TmX_User_ID, UserName, Detail)
SELECT  @MigrationActor,
        ''OrphanAuthStubbed'',
        a.Id,
        a.Id,
        a.UserName,
        ''Stub TmX_User created with Active_Flag=0. Admin must complete profile (tenant, location, branch, role) before user can transact.''
FROM    [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
INNER   JOIN [ICBC_DEMO].[dbo].[TmX_User]    t
        ON   t.User_ID    = a.Id
       AND   t.Created_By = @MigrationActor
WHERE   NOT EXISTS (
            SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log] L
            WHERE  L.Migration_Code = @MigrationActor
              AND  L.Reason         = ''OrphanAuthStubbed''
              AND  L.AspNet_Id      = a.Id);
SELECT @rowcount = @@ROWCOUNT;
';
    EXEC sp_executesql @Sql,
         N'@MigrationActor VARCHAR(40), @rowcount INT OUTPUT',
         @MigrationActor = @MigrationActor,
         @rowcount       = @Stage3Logged OUTPUT;
    PRINT '  [log] Logged ' + CAST(@Stage3Logged AS VARCHAR(10)) + ' stub row(s) for admin attention';
END;

----------------------------------------------------------------------
-- STAGE 4: LOG ORPHAN TMX USERS (no credentials)
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 4 -- Log orphan TmX_User rows (no credentials)';
PRINT @SeparatorRow;

SET @Sql = N'
INSERT INTO [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log]
    (Migration_Code, Reason, AspNet_Id, TmX_User_ID, UserName, Detail)
SELECT  @MigrationActor,
        ''OrphanTmxNoCredentials'',
        NULL,
        t.User_ID,
        t.User_Name,
        ''TmX_User has no matching AspNetUsers row. PasswordHash is NULL. Admin must issue a password reset before this user can log in via JWT.''
FROM    [ICBC_DEMO].[dbo].[TmX_User] t
WHERE   t.PasswordHash IS NULL
  AND   NOT EXISTS (SELECT 1 FROM [ICBC_DEMO_AUTH].[dbo].[AspNetUsers] a
                    WHERE  UPPER(a.UserName) = UPPER(t.User_Name))
  AND   NOT EXISTS (
            SELECT 1 FROM [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log] L
            WHERE  L.Migration_Code = @MigrationActor
              AND  L.Reason         = ''OrphanTmxNoCredentials''
              AND  L.TmX_User_ID    = t.User_ID);
SELECT @rowcount = @@ROWCOUNT;
';

EXEC sp_executesql @Sql,
     N'@MigrationActor VARCHAR(40), @rowcount INT OUTPUT',
     @MigrationActor = @MigrationActor,
     @rowcount       = @Stage4Logged OUTPUT;

PRINT '  [log] Logged ' + CAST(@Stage4Logged AS VARCHAR(10)) + ' orphan TmX_User row(s) needing admin password reset';

----------------------------------------------------------------------
-- STAGE 5: STATUS SYNC FOR REMAINING NULL ROWS
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 5 -- Backfill Status from Active_Flag where NULL';
PRINT @SeparatorRow;

SET @Sql = N'
UPDATE  [ICBC_DEMO].[dbo].[TmX_User]
SET     [Status]          = CASE WHEN Active_Flag = 1 THEN N''Active'' ELSE N''InActive'' END,
        Last_Updated_Date = GETUTCDATE(),
        Last_Updated_By   = @MigrationActor
WHERE   [Status] IS NULL;
SELECT @rowcount = @@ROWCOUNT;
';

EXEC sp_executesql @Sql,
     N'@MigrationActor VARCHAR(40), @rowcount INT OUTPUT',
     @MigrationActor = @MigrationActor,
     @rowcount       = @Stage5Updated OUTPUT;

PRINT '  [upd] Set Status on ' + CAST(@Stage5Updated AS VARCHAR(10)) + ' row(s)';

----------------------------------------------------------------------
-- STAGE 6: POST-MIGRATION VALIDATION
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 6 -- Post-migration validation';
PRINT @SeparatorRow;

SET @Sql = N'
SELECT @cntAll      = COUNT(*) FROM [ICBC_DEMO].[dbo].[TmX_User];
SELECT @cntWithHash = COUNT(*) FROM [ICBC_DEMO].[dbo].[TmX_User] WHERE PasswordHash IS NOT NULL;
SELECT @cntNullHash = COUNT(*) FROM [ICBC_DEMO].[dbo].[TmX_User] WHERE PasswordHash IS NULL;
SELECT @cntStatusN  = COUNT(*) FROM [ICBC_DEMO].[dbo].[TmX_User] WHERE [Status] IS NULL;
';

EXEC sp_executesql @Sql,
     N'@cntAll INT OUTPUT, @cntWithHash INT OUTPUT, @cntNullHash INT OUTPUT, @cntStatusN INT OUTPUT',
     @cntAll      = @TmxCountAfter OUTPUT,
     @cntWithHash = @TmxWithHash   OUTPUT,
     @cntNullHash = @TmxNullHash   OUTPUT,
     @cntStatusN  = @TmxStatusNull OUTPUT;

DECLARE @ExpectedTmxAfter INT = @TmxCount + @Stage3Inserted;

PRINT '  TmX_User counts after migration:';
PRINT '    Total rows:                            ' + CAST(@TmxCountAfter AS VARCHAR(10))
                                                    + '  (expected ' + CAST(@ExpectedTmxAfter AS VARCHAR(10)) + ')';
PRINT '    Rows with PasswordHash:                ' + CAST(@TmxWithHash   AS VARCHAR(10));
PRINT '    Rows with NULL PasswordHash:           ' + CAST(@TmxNullHash   AS VARCHAR(10))
                                                    + '  (these need admin password reset)';
PRINT '    Rows with NULL Status:                 ' + CAST(@TmxStatusNull AS VARCHAR(10))
                                                    + '  (must be 0)';

IF @TmxCountAfter <> @ExpectedTmxAfter
BEGIN
    PRINT '  [fail] Final row count does not match expected -- investigate.';
    SET @ValidationFailed = 1;
END;

IF @TmxStatusNull > 0
BEGIN
    PRINT '  [fail] Some rows still have NULL Status -- Stage 5 should have caught these.';
    SET @ValidationFailed = 1;
END;

IF @ValidationFailed = 0
    PRINT '  [ok] Validation passed';

----------------------------------------------------------------------
-- STAGE 7: COMMIT OR ROLLBACK
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 7 -- Transaction outcome';
PRINT @SeparatorRow;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION MergeAuthData;
    PRINT '  DRY RUN -- data transaction ROLLED BACK. Schema additions remain (idempotent).';
    PRINT '  Re-run with @DryRun = 0 to commit data changes.';
END
ELSE IF @ValidationFailed = 1
BEGIN
    ROLLBACK TRANSACTION MergeAuthData;
    PRINT '  VALIDATION FAILED -- transaction ROLLED BACK. Inspect the report and fix.';
END
ELSE
BEGIN
    COMMIT TRANSACTION MergeAuthData;
    PRINT '  MIGRATION COMMITTED.';
    PRINT '';
    PRINT '  Next steps:';
    PRINT '    1. Inspect [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log] for stub/orphan rows.';
    PRINT '    2. Admins complete profiles (Tenant_ID, Location_ID, branch, role) for stubs.';
    PRINT '    3. Admins issue password resets for OrphanTmxNoCredentials rows.';
    PRINT '    4. Do NOT drop AspNet* tables yet -- legacy backend still reads them.';
    PRINT '    5. Schedule 2026_05_002_DropAspNetTables.sql for T+30 days post-cutover.';
END;

PRINT '';
PRINT @SeparatorBig;
PRINT '  Finished:   ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;

----------------------------------------------------------------------
-- HELPFUL POST-RUN QUERIES (uncomment to inspect after a real run)
----------------------------------------------------------------------
-- View reconciliation entries from this run
/*
SELECT *
FROM   [ICBC_DEMO].[dbo].[TmX_User_Reconciliation_Log]
WHERE  Migration_Code = 'AUTH_MERGE_2026_05_001'
ORDER  BY Reason, Logged_At;
*/

-- All TmX_User rows that need admin attention
/*
SELECT t.User_ID, t.User_Name, t.Active_Flag, t.[Status],
       CASE WHEN t.PasswordHash IS NULL THEN 'NEEDS RESET' ELSE 'OK' END AS Credential_Status,
       t.Tenant_ID, t.Location_ID, t.Created_By
FROM   [ICBC_DEMO].[dbo].[TmX_User] t
WHERE  t.PasswordHash IS NULL
   OR  t.Created_By   = 'AUTH_MERGE_2026_05_001'
ORDER  BY t.Created_By, t.User_Name;
*/

-- Spot-check 20 random matched rows
/*
SELECT TOP 20 t.User_ID, t.User_Name,
              t.Active_Flag, t.[Status],
              CASE WHEN t.PasswordHash IS NULL THEN 'NULL' ELSE 'set' END AS Hash,
              t.LockoutEnabled, t.AccessFailedCount, t.TwoFactorEnabled,
              t.PasswordExpiryDate, t.LastLoginDate, t.FirstPasswordChange
FROM   [ICBC_DEMO].[dbo].[TmX_User] t
WHERE  t.Created_By <> 'AUTH_MERGE_2026_05_001'
ORDER  BY NEWID();
*/
