-- =========================================================================
-- 2026_07_044_CreateTmxNotificationPref.sql
-- -------------------------------------------------------------------------
-- Notification system (Phase 2) — per-user delivery preferences. Creates
-- dbo.TmX_Notification_Pref: one row per (user, notification type) the user
-- has explicitly toggled. The emitter (INotificationService) reads this to
-- drop recipients who have disabled in-app delivery for a type; the default
-- is ON, so a user with no row keeps receiving every type.
--
-- User_Id stores a user id OR a username — same Recipient_User semantics as
-- TmX_Notification (matched against ICurrentUserService.UserId / .UserName),
-- so preferences line up with the feed without a lookup.
--
-- Idempotent: table + unique index guarded by OBJECT_ID / inside the create
-- guard. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_044_CreateTmxNotificationPref.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Table ----------
IF OBJECT_ID('dbo.TmX_Notification_Pref','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Notification_Pref (
        Notification_Pref_ID bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Notification_Pref PRIMARY KEY,
        User_Id           nvarchar(256) NOT NULL,   -- a user id OR username; matches Recipient_User semantics
        Type_Code         nvarchar(40)  NOT NULL,
        In_App_Enabled    bit           NOT NULL CONSTRAINT DF_TmX_NotifPref_InApp   DEFAULT(1),
        Created_Date      datetime2(3)  NOT NULL CONSTRAINT DF_TmX_NotifPref_Created DEFAULT(SYSUTCDATETIME()),
        Last_Updated_Date datetime2(3)  NULL
    );
    CREATE UNIQUE INDEX UX_TmX_NotifPref_User_Type
        ON dbo.TmX_Notification_Pref (User_Id, Type_Code);
    PRINT 'Created dbo.TmX_Notification_Pref.';
END
GO

PRINT '2026_07_044 complete - TmX_Notification_Pref schema created.';
GO
