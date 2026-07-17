/* ============================================================================
   2026_07_026_AddUserLockoutLifecycleColumns.sql

   Adds the account lockout / lifecycle telemetry columns to dbo.TmX_User.

   These columns are mapped by ApplicationUserConfiguration.cs (lines 43-51)
   and consumed by the Users Lock/Unlock/ResetPassword/Deactivate handlers and
   the Login flow. They were introduced additively in code (the entity comment
   points at "2026_07_002") but no numbered migration ever created them —
   2026_07_002 is actually the AutoFlag aggregate engine. On any DB provisioned
   from the numbered migrations (e.g. UAT) the columns are therefore absent, so
   EF's TmX_User model binding fails with errors such as
   "Invalid column name 'Last_Failed_Login_Date'" and the User Management API
   throws on the first query. This migration closes that gap.

   All columns are additive and nullable (except Must_Change_Password, which is
   NOT NULL with a DEFAULT 0 so existing rows backfill safely). Types match the
   legacy TmX_User convention: date/time as [datetime] (not datetime2), and the
   EF-configured nvarchar lengths (Lockout_Reason 30; *_By 100).

   Idempotent — every column is guarded by IF NOT EXISTS(sys.columns). Safe to
   re-run and safe on a DB that already has some/all of the columns.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* Last_Failed_Login_Date --------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Last_Failed_Login_Date')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Last_Failed_Login_Date] datetime NULL;
    PRINT 'Added dbo.TmX_User.Last_Failed_Login_Date.';
END
ELSE PRINT 'dbo.TmX_User.Last_Failed_Login_Date already exists - skipped.';
GO

/* Lockout_Reason ('Auto' | 'AdminLock') ------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Lockout_Reason')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Lockout_Reason] nvarchar(30) NULL;
    PRINT 'Added dbo.TmX_User.Lockout_Reason.';
END
ELSE PRINT 'dbo.TmX_User.Lockout_Reason already exists - skipped.';
GO

/* Locked_At ---------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Locked_At')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Locked_At] datetime NULL;
    PRINT 'Added dbo.TmX_User.Locked_At.';
END
ELSE PRINT 'dbo.TmX_User.Locked_At already exists - skipped.';
GO

/* Locked_By ---------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Locked_By')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Locked_By] nvarchar(100) NULL;
    PRINT 'Added dbo.TmX_User.Locked_By.';
END
ELSE PRINT 'dbo.TmX_User.Locked_By already exists - skipped.';
GO

/* Unlocked_At -------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Unlocked_At')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Unlocked_At] datetime NULL;
    PRINT 'Added dbo.TmX_User.Unlocked_At.';
END
ELSE PRINT 'dbo.TmX_User.Unlocked_At already exists - skipped.';
GO

/* Unlocked_By -------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Unlocked_By')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Unlocked_By] nvarchar(100) NULL;
    PRINT 'Added dbo.TmX_User.Unlocked_By.';
END
ELSE PRINT 'dbo.TmX_User.Unlocked_By already exists - skipped.';
GO

/* Deactivated_At ----------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Deactivated_At')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Deactivated_At] datetime NULL;
    PRINT 'Added dbo.TmX_User.Deactivated_At.';
END
ELSE PRINT 'dbo.TmX_User.Deactivated_At already exists - skipped.';
GO

/* Deactivated_By ----------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Deactivated_By')
BEGIN
    ALTER TABLE dbo.TmX_User ADD [Deactivated_By] nvarchar(100) NULL;
    PRINT 'Added dbo.TmX_User.Deactivated_By.';
END
ELSE PRINT 'dbo.TmX_User.Deactivated_By already exists - skipped.';
GO

/* Must_Change_Password (NOT NULL, DEFAULT 0 backfills existing rows) ------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_User') AND name = 'Must_Change_Password')
BEGIN
    ALTER TABLE dbo.TmX_User
        ADD [Must_Change_Password] bit NOT NULL
            CONSTRAINT [DF_TmX_User_Must_Change_Password] DEFAULT (0);
    PRINT 'Added dbo.TmX_User.Must_Change_Password (default 0).';
END
ELSE PRINT 'dbo.TmX_User.Must_Change_Password already exists - skipped.';
GO

PRINT '2026_07_026_AddUserLockoutLifecycleColumns.sql complete.';
GO
