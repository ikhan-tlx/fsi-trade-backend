-- =========================================================================
-- 2026_07_043_CreateTmxNotification.sql
-- -------------------------------------------------------------------------
-- Notification system (backend read-model). Creates dbo.TmX_Notification —
-- the per-recipient notification feed populated by the app's emitter
-- (INotificationService) after maker/checker approvals, request decisions
-- and workflow task assignments, and read back by the /Notifications API.
--
-- Recipient_User stores a user id OR a username; reads match against both
-- (ICurrentUserService.UserId / .UserName), so the emitter can raise against
-- whichever identifier it holds without a lookup.
--
-- Idempotent: table + index guarded by OBJECT_ID / inside the create guard.
-- Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_043_CreateTmxNotification.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Table ----------
IF OBJECT_ID('dbo.TmX_Notification','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Notification (
        Notification_ID bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Notification PRIMARY KEY,
        Tenant_ID       int           NULL,
        Recipient_User  nvarchar(256) NOT NULL,   -- a user id OR username; reads match against both
        Type_Code       nvarchar(40)  NOT NULL,
        Severity        nvarchar(20)  NOT NULL CONSTRAINT DF_TmX_Notif_Sev  DEFAULT('info'),
        Title           nvarchar(200) NOT NULL,
        Body            nvarchar(1000) NULL,
        Entity_Type     nvarchar(60)  NULL,
        Entity_Id       nvarchar(100) NULL,
        Deep_Link       nvarchar(300) NULL,
        Is_Read         bit           NOT NULL CONSTRAINT DF_TmX_Notif_Read DEFAULT(0),
        Read_Date       datetime2(3)  NULL,
        Group_Key       nvarchar(100) NULL,
        Created_By      nvarchar(256) NULL,        -- the actor who caused it
        Created_Date    datetime2(3)  NOT NULL CONSTRAINT DF_TmX_Notif_Date DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_TmX_Notif_Recipient
        ON dbo.TmX_Notification (Recipient_User, Is_Read, Created_Date DESC);
    PRINT 'Created dbo.TmX_Notification.';
END
GO

PRINT '2026_07_043 complete - TmX_Notification schema created.';
GO
