/*============================================================================
  Migration:  2026_05_004_CreateUserDeviceAndLoginAudit.sql
  Purpose:    Device registry + login audit + bind RefreshTokens to a device.

  Tables created in [ICBC_DEMO]:
    - UserDevices   : current-state registry of devices per user
                      (named UserDevices, NOT TmX_User_Device — the latter is
                       a pre-existing legacy junction table with a different
                       schema)
    - TmX_Login_Audit   : append-only audit log for auth lifecycle events

  Existing table altered:
    - RefreshTokens     : add Device_ID NVARCHAR(64) NULL  (FK to UserDevices)

  Idempotent: yes. Re-runs are no-ops.

  Notes on column types:
    - Device_ID  : NVARCHAR(64) — server-issued opaque value (URL-safe base64
                   of 48 random bytes; same shape as RefreshTokens.Id).
    - User_ID    : type discovered at runtime from TmX_User to avoid the
                   length-mismatch class of FK errors we hit earlier.

  Author:     slice-1.5, ICBC Backend Modernisation programme
============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SeparatorBig VARCHAR(80) = REPLICATE('=', 70);
DECLARE @SeparatorRow VARCHAR(80) = REPLICATE('-', 70);

PRINT @SeparatorBig;
PRINT '  Migration: 2026_05_004_CreateUserDeviceAndLoginAudit';
PRINT '  Started:   ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;

----------------------------------------------------------------------
-- STAGE 0: PREFLIGHT
----------------------------------------------------------------------
IF DB_ID('ICBC_DEMO') IS NULL
    THROW 50001, 'Database [ICBC_DEMO] not found.', 1;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
               WHERE name = 'TmX_User' AND SCHEMA_NAME(schema_id) = 'dbo')
    THROW 50002, '[ICBC_DEMO].[dbo].[TmX_User] not found. Run 2026_05_001 first.', 1;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
               WHERE name = 'RefreshTokens' AND SCHEMA_NAME(schema_id) = 'dbo')
    THROW 50003, '[ICBC_DEMO].[dbo].[RefreshTokens] not found. Run 2026_05_002 first.', 1;

PRINT '  [ok] Preflight passed';

----------------------------------------------------------------------
-- STAGE 1: DETECT TmX_User.User_ID DATA TYPE (FK type-match)
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 1 -- Detect TmX_User.User_ID type';
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
    THROW 50004, 'Could not detect [TmX_User].[User_ID] data type.', 1;

PRINT '  [info] TmX_User.User_ID detected as ' + @UserIdTypeDef;

----------------------------------------------------------------------
-- STAGE 2: CREATE [UserDevices]
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 2 -- Create [UserDevices]';
PRINT @SeparatorRow;

IF EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
           WHERE name = 'UserDevices' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    PRINT '  [ok] Already exists';
END
ELSE
BEGIN
    DECLARE @CreateDeviceSql NVARCHAR(MAX) = N'
CREATE TABLE [ICBC_DEMO].[dbo].[UserDevices] (
    Device_ID         NVARCHAR(64)   NOT NULL,
    User_ID           ' + @UserIdTypeDef + N' NOT NULL,
    Label             NVARCHAR(200)  NULL,
    User_Agent        NVARCHAR(500)  NULL,
    First_Seen_At     DATETIME       NOT NULL CONSTRAINT DF_UserDevices_FirstSeen DEFAULT (GETUTCDATE()),
    Last_Seen_At      DATETIME       NOT NULL CONSTRAINT DF_UserDevices_LastSeen  DEFAULT (GETUTCDATE()),
    First_Seen_Ip     NVARCHAR(45)   NULL,
    Last_Seen_Ip      NVARCHAR(45)   NULL,
    Is_Trusted        BIT            NOT NULL CONSTRAINT DF_UserDevices_IsTrusted DEFAULT (0),
    Revoked_At        DATETIME       NULL,
    Revoke_Reason     NVARCHAR(100)  NULL,

    CONSTRAINT PK_UserDevices PRIMARY KEY (Device_ID),

    CONSTRAINT FK_UserDevices_TmX_User
        FOREIGN KEY (User_ID) REFERENCES [ICBC_DEMO].[dbo].[TmX_User] (User_ID)
);

CREATE INDEX IX_UserDevices_User_ID
    ON [ICBC_DEMO].[dbo].[UserDevices] (User_ID, Last_Seen_At DESC)
    WHERE Revoked_At IS NULL;
';
    EXEC sp_executesql @CreateDeviceSql;
    PRINT '  [add] Created [UserDevices] (User_ID typed as ' + @UserIdTypeDef + ')';
END;

----------------------------------------------------------------------
-- STAGE 3: CREATE [TmX_Login_Audit]
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 3 -- Create [TmX_Login_Audit]';
PRINT @SeparatorRow;

IF EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
           WHERE name = 'TmX_Login_Audit' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    PRINT '  [ok] Already exists';
END
ELSE
BEGIN
    DECLARE @CreateAuditSql NVARCHAR(MAX) = N'
CREATE TABLE [ICBC_DEMO].[dbo].[TmX_Login_Audit] (
    Audit_ID          BIGINT IDENTITY(1,1) NOT NULL,
    User_ID           ' + @UserIdTypeDef + N' NULL,
    Username_Attempt  NVARCHAR(256)  NULL,
    Device_ID         NVARCHAR(64)   NULL,
    Ip_Address        NVARCHAR(45)   NULL,
    User_Agent        NVARCHAR(500)  NULL,
    Action            VARCHAR(40)    NOT NULL,
    Result            VARCHAR(40)    NOT NULL,
    Detail            NVARCHAR(MAX)  NULL,
    Created_At        DATETIME       NOT NULL CONSTRAINT DF_TmX_Login_Audit_CreatedAt DEFAULT (GETUTCDATE()),

    CONSTRAINT PK_TmX_Login_Audit PRIMARY KEY (Audit_ID)
);

CREATE INDEX IX_TmX_Login_Audit_User_ID  ON [ICBC_DEMO].[dbo].[TmX_Login_Audit] (User_ID, Created_At DESC);
CREATE INDEX IX_TmX_Login_Audit_DeviceId ON [ICBC_DEMO].[dbo].[TmX_Login_Audit] (Device_ID, Created_At DESC);
CREATE INDEX IX_TmX_Login_Audit_Created  ON [ICBC_DEMO].[dbo].[TmX_Login_Audit] (Created_At);
';
    EXEC sp_executesql @CreateAuditSql;
    PRINT '  [add] Created [TmX_Login_Audit] with 3 indexes';
END;

----------------------------------------------------------------------
-- STAGE 4: ALTER [RefreshTokens] — bind to a device
--
-- Defensive: read the actual type of UserDevices.Device_ID and ensure
-- RefreshTokens.Device_ID matches exactly. If there's a type mismatch from
-- a previous half-baked run, we drop+re-add the column so the FK can land.
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 4 -- Add Device_ID column to [RefreshTokens]';
PRINT @SeparatorRow;

-- Detect the live type of the referenced column
DECLARE @DeviceIdTypeDef NVARCHAR(100), @DeviceIdMaxLen INT, @DeviceIdTypeName SYSNAME;

SELECT @DeviceIdTypeName = ty.name,
       @DeviceIdMaxLen   = c.max_length,
       @DeviceIdTypeDef =
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
WHERE  c.object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[UserDevices]')
  AND  c.name      = N'Device_ID';

IF @DeviceIdTypeDef IS NULL
    THROW 50005, 'Could not detect [UserDevices].[Device_ID] type. Stage 2 must have failed.', 1;

PRINT '  [info] UserDevices.Device_ID detected as ' + @DeviceIdTypeDef;

-- If RefreshTokens.Device_ID exists but does NOT match, drop the dependent
-- objects (FK + index) and the column, so we can recreate it correctly.
DECLARE @ExistingTypeName SYSNAME, @ExistingMaxLen INT;

SELECT @ExistingTypeName = ty.name,
       @ExistingMaxLen   = c.max_length
FROM   [ICBC_DEMO].sys.columns c
JOIN   [ICBC_DEMO].sys.types   ty ON ty.user_type_id = c.user_type_id
                                 AND ty.is_user_defined = 0
WHERE  c.object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[RefreshTokens]')
  AND  c.name      = N'Device_ID';

DECLARE @TypeMismatch BIT = 0;

IF @ExistingTypeName IS NOT NULL
   AND (@ExistingTypeName <> @DeviceIdTypeName OR @ExistingMaxLen <> @DeviceIdMaxLen)
BEGIN
    SET @TypeMismatch = 1;
    PRINT '  [warn] RefreshTokens.Device_ID exists with type '
          + @ExistingTypeName + ' (max_length ' + CAST(@ExistingMaxLen AS VARCHAR(10)) + ')'
          + ' which does not match the referenced column. Realigning.';

    IF EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.foreign_keys WHERE name = 'FK_RefreshTokens_UserDevices')
        ALTER TABLE [ICBC_DEMO].[dbo].[RefreshTokens] DROP CONSTRAINT FK_RefreshTokens_UserDevices;

    IF EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.indexes
               WHERE name = 'IX_RefreshTokens_Device_ID'
                 AND object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[RefreshTokens]'))
        DROP INDEX IX_RefreshTokens_Device_ID ON [ICBC_DEMO].[dbo].[RefreshTokens];

    ALTER TABLE [ICBC_DEMO].[dbo].[RefreshTokens] DROP COLUMN Device_ID;
    SET @ExistingTypeName = NULL;
END;

-- Add the column with the matching type if it doesn't exist.
IF @ExistingTypeName IS NULL
BEGIN
    DECLARE @AddColSql NVARCHAR(MAX) = N'
ALTER TABLE [ICBC_DEMO].[dbo].[RefreshTokens]
    ADD Device_ID ' + @DeviceIdTypeDef + N' NULL;';
    EXEC sp_executesql @AddColSql;
    PRINT '  [add] Added RefreshTokens.Device_ID ' + @DeviceIdTypeDef + ' NULL';
END
ELSE IF @TypeMismatch = 0
BEGIN
    PRINT '  [ok] RefreshTokens.Device_ID already exists with matching type';
END;

-- FK
IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.foreign_keys
               WHERE name = 'FK_RefreshTokens_UserDevices')
BEGIN
    ALTER TABLE [ICBC_DEMO].[dbo].[RefreshTokens]
        ADD CONSTRAINT FK_RefreshTokens_UserDevices
            FOREIGN KEY (Device_ID) REFERENCES [ICBC_DEMO].[dbo].[UserDevices] (Device_ID);
    PRINT '  [add] FK RefreshTokens.Device_ID -> UserDevices.Device_ID';
END
ELSE
    PRINT '  [ok] FK already present';

-- Index
IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.indexes
               WHERE name = 'IX_RefreshTokens_Device_ID'
                 AND object_id = OBJECT_ID(N'[ICBC_DEMO].[dbo].[RefreshTokens]'))
BEGIN
    CREATE INDEX IX_RefreshTokens_Device_ID
        ON [ICBC_DEMO].[dbo].[RefreshTokens] (Device_ID)
        WHERE Revoked_At IS NULL;
    PRINT '  [add] Index IX_RefreshTokens_Device_ID';
END
ELSE
    PRINT '  [ok] Index already present';

----------------------------------------------------------------------
-- DONE
----------------------------------------------------------------------
PRINT '';
PRINT @SeparatorBig;
PRINT '  Finished:  ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;
