/* ============================================================================
   2026_07_002_UserLifecycleAndLockoutFields
   ----------------------------------------------------------------------------
   Additive, forward-only. Adds account-lifecycle & lockout telemetry columns to
   dbo.TmX_User so admins get first-class lock/reset/deactivation data instead of
   reconstructing it from the audit log.

   NOTHING is dropped or renamed here (see the redesign doc for the later
   deprecation of Status / LockoutEnabled). Every add is guarded so the script
   is idempotent and safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;

-- Last failed login timestamp (successful login already tracked in LastLoginDate).
IF COL_LENGTH('dbo.TmX_User', 'Last_Failed_Login_Date') IS NULL
    ALTER TABLE dbo.TmX_User ADD Last_Failed_Login_Date datetime NULL;

-- Why the account is locked: 'Auto' (failed attempts) vs 'AdminLock' (manual).
IF COL_LENGTH('dbo.TmX_User', 'Lockout_Reason') IS NULL
    ALTER TABLE dbo.TmX_User ADD Lockout_Reason nvarchar(30) NULL;

-- When/who locked (manual lock or the moment auto-lock tripped).
IF COL_LENGTH('dbo.TmX_User', 'Locked_At') IS NULL
    ALTER TABLE dbo.TmX_User ADD Locked_At datetime NULL;
IF COL_LENGTH('dbo.TmX_User', 'Locked_By') IS NULL
    ALTER TABLE dbo.TmX_User ADD Locked_By nvarchar(100) NULL;

-- When/who unlocked.
IF COL_LENGTH('dbo.TmX_User', 'Unlocked_At') IS NULL
    ALTER TABLE dbo.TmX_User ADD Unlocked_At datetime NULL;
IF COL_LENGTH('dbo.TmX_User', 'Unlocked_By') IS NULL
    ALTER TABLE dbo.TmX_User ADD Unlocked_By nvarchar(100) NULL;

-- When/who deactivated (admin disable).
IF COL_LENGTH('dbo.TmX_User', 'Deactivated_At') IS NULL
    ALTER TABLE dbo.TmX_User ADD Deactivated_At datetime NULL;
IF COL_LENGTH('dbo.TmX_User', 'Deactivated_By') IS NULL
    ALTER TABLE dbo.TmX_User ADD Deactivated_By nvarchar(100) NULL;

-- Generic "must change password on next login" — covers first-login AND
-- admin-forced resets. Backfilled from the existing FirstPasswordChange flag.
IF COL_LENGTH('dbo.TmX_User', 'Must_Change_Password') IS NULL
    ALTER TABLE dbo.TmX_User ADD Must_Change_Password bit NOT NULL
        CONSTRAINT DF_TmX_User_MustChangePassword DEFAULT (0);
GO

-- Backfill Must_Change_Password from FirstPasswordChange (one-time).
IF COL_LENGTH('dbo.TmX_User', 'Must_Change_Password') IS NOT NULL
   AND COL_LENGTH('dbo.TmX_User', 'FirstPasswordChange') IS NOT NULL
BEGIN
    UPDATE dbo.TmX_User
       SET Must_Change_Password = FirstPasswordChange
     WHERE Must_Change_Password <> FirstPasswordChange;
END
GO

-- Backfill Lockout_Reason for rows already locked when this ships.
IF COL_LENGTH('dbo.TmX_User', 'Lockout_Reason') IS NOT NULL
BEGIN
    UPDATE dbo.TmX_User
       SET Lockout_Reason = 'Auto'
     WHERE Lockout_Reason IS NULL
       AND LockoutEndDateUtc IS NOT NULL
       AND LockoutEndDateUtc > GETUTCDATE();
END
GO
