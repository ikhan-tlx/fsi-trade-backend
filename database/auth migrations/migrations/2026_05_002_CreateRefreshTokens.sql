/*============================================================================
  Migration:  2026_05_002_CreateRefreshTokens.sql
  Purpose:    Create [ICBC_DEMO].[dbo].[RefreshTokens] for JWT refresh tokens.

  How the FK type is decided:
              The script reads the real data type of [TmX_User].[User_ID]
              from sys.columns and builds the new RefreshTokens.User_ID
              column to match exactly. This avoids any human guess about
              whether it is NVARCHAR(50), NVARCHAR(100), VARCHAR(40), or
              anything else, and guarantees the FK creates first time.

  Idempotent: yes -- IF NOT EXISTS guard.

  Author:     planning step 1, ICBC Backend Modernisation programme
============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SeparatorBig VARCHAR(80) = REPLICATE('=', 70);
DECLARE @SeparatorRow VARCHAR(80) = REPLICATE('-', 70);

PRINT @SeparatorBig;
PRINT '  Migration: 2026_05_002_CreateRefreshTokens';
PRINT '  Started:   ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;

----------------------------------------------------------------------
-- STAGE 0 -- PREFLIGHT
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 0 -- Preflight';
PRINT @SeparatorRow;

IF DB_ID('ICBC_DEMO') IS NULL
    THROW 50001, 'Database [ICBC_DEMO] not found on this instance.', 1;

IF NOT EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
               WHERE name = 'TmX_User' AND SCHEMA_NAME(schema_id) = 'dbo')
    THROW 50002, '[ICBC_DEMO].[dbo].[TmX_User] not found. Run 2026_05_001 first.', 1;

PRINT '  [ok] Database and TmX_User present';

----------------------------------------------------------------------
-- STAGE 1 -- DISCOVER ACTUAL TYPE OF TmX_User.User_ID
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 1 -- Detect TmX_User.User_ID data type';
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
    THROW 50003, 'Could not detect [TmX_User].[User_ID] data type via sys.columns.', 1;

PRINT '  [info] Detected TmX_User.User_ID type: ' + @UserIdTypeDef;

----------------------------------------------------------------------
-- STAGE 2 -- CREATE TABLE (idempotent, type-matched FK)
----------------------------------------------------------------------
PRINT '';
PRINT 'STAGE 2 -- Create [RefreshTokens]';
PRINT @SeparatorRow;

IF EXISTS (SELECT 1 FROM [ICBC_DEMO].sys.tables
           WHERE name = 'RefreshTokens' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    PRINT '  [ok] [RefreshTokens] already exists -- no change';
END
ELSE
BEGIN
    DECLARE @CreateSql NVARCHAR(MAX) = N'
CREATE TABLE [ICBC_DEMO].[dbo].[RefreshTokens] (
    Id                NVARCHAR(64)   NOT NULL,
    User_ID           ' + @UserIdTypeDef + N' NOT NULL,
    Issued_At         DATETIME       NOT NULL CONSTRAINT DF_RefreshTokens_IssuedAt DEFAULT (GETUTCDATE()),
    Expires_At        DATETIME       NOT NULL,
    Revoked_At        DATETIME       NULL,
    Replaced_By       NVARCHAR(64)   NULL,
    Revoke_Reason     NVARCHAR(100)  NULL,
    Created_By_Ip     NVARCHAR(45)   NULL,
    Revoked_By_Ip     NVARCHAR(45)   NULL,

    CONSTRAINT PK_RefreshTokens
        PRIMARY KEY (Id),

    CONSTRAINT FK_RefreshTokens_TmX_User
        FOREIGN KEY (User_ID)
        REFERENCES  [ICBC_DEMO].[dbo].[TmX_User] (User_ID),

    CONSTRAINT CK_RefreshTokens_RevokedAfterIssued
        CHECK (Revoked_At IS NULL OR Revoked_At >= Issued_At),

    CONSTRAINT CK_RefreshTokens_ExpiresAfterIssued
        CHECK (Expires_At >= Issued_At)
);

CREATE INDEX IX_RefreshTokens_User_ID
    ON [ICBC_DEMO].[dbo].[RefreshTokens] (User_ID)
    INCLUDE (Expires_At, Revoked_At);

CREATE INDEX IX_RefreshTokens_Expires_At
    ON [ICBC_DEMO].[dbo].[RefreshTokens] (Expires_At)
    WHERE Revoked_At IS NULL;
';

    EXEC sp_executesql @CreateSql;

    PRINT '  [add] Created [RefreshTokens] (User_ID typed as ' + @UserIdTypeDef + ')';
    PRINT '         + 2 CHECK constraints, 1 FK to TmX_User, 2 indexes';
END;

PRINT '';
PRINT @SeparatorBig;
PRINT '  Finished:  ' + CONVERT(VARCHAR(30), SYSUTCDATETIME(), 121) + ' UTC';
PRINT @SeparatorBig;
