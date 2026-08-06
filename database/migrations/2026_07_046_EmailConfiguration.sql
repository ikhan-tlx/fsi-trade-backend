-- =========================================================================
-- 2026_07_046_EmailConfiguration.sql
-- -------------------------------------------------------------------------
-- Email / SMTP settings for the Configuration Management module
-- (/admin/configuration). Adds a new 'email' category plus 9 definitions
-- (enabled, delivery mode, pickup directory, host, port, username, use-ssl,
-- from address, min severity) and seeds their Global values + a Create
-- history row each.
--
-- These keys drive the notification email channel, read via the
-- DB->IConfiguration bridge as IOptionsMonitor<EmailOptions> (SectionName
-- "Email" -> Email.* keys, hot-reload), so an operator can turn email on/off,
-- switch delivery mode and re-point host/port/from from this screen with no
-- restart. Editable, non-sensitive and apply immediately
-- (Requires_Approval = 0), so the existing PUT /api/v1/Configuration write
-- path (Configuration.Manage) manages them with no maker/checker step.
--
-- SECRET stays out of the DB bridge: the SMTP PASSWORD (Email.Password) is
-- NOT seeded here — it remains in appsettings.json because the bridge excludes
-- Is_Sensitive keys and the file's policy keeps secrets in the file.
-- Until this migration is run, EmailOptions falls back to its C# defaults for
-- these keys (Enabled defaults to false), so email is OFF until seeded.
--
-- GLOBAL only: the TmX_Configuration_* tables have no tenant column; every
-- value row is seeded with Branch_ID / Role_ID / Environment = NULL.
--
-- Idempotent: category via MERGE (WHEN NOT MATCHED); definitions / values /
-- history guarded by NOT EXISTS on their natural keys. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_046_EmailConfiguration.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Seed category ----------
MERGE dbo.TmX_Configuration_Category AS tgt
USING (VALUES
    ('email', N'Email / SMTP', N'Notification email delivery: on/off, delivery mode, SMTP host/port and sender', 17, NULL, 0, 0)
) AS src (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
ON tgt.Category_Key = src.Category_Key
WHEN NOT MATCHED THEN
    INSERT (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
    VALUES (src.Category_Key, src.Title, src.Description, src.Display_Order, src.Verify_Kind, src.Is_Flagged, src.Is_Logging);
PRINT CONCAT('Section 1 - email category upserted: ', @@ROWCOUNT);
GO

-- ---------- 2. Seed definitions + Global values ----------
DECLARE @who nvarchar(100) = N'System (2026_07_046)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- staging: one row per email setting (Value = the current Global value = Default_Value)
DECLARE @D TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Val nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @D (K,Name,Descr,Cat,Dt,Def,Val,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Email.Enabled',         N'Email enabled',      N'Master on/off for the notification email channel. When off, email sending is a no-op.',                    N'email', N'Boolean', N'true',                       N'true',                       NULL, NULL,                                                        1, 0, 0, 0, 10),
('Email.DeliveryMode',    N'Delivery mode',      N'Transport: PickupDirectory writes .eml files to the pickup folder (no SMTP); Smtp sends over the network.', N'email', N'Enum',    N'PickupDirectory',            N'PickupDirectory',            NULL, N'{"allowedValues":["PickupDirectory","Smtp"]}',              1, 0, 0, 0, 20),
('Email.PickupDirectory', N'Pickup directory',   N'Folder that receives generated .eml files when delivery mode is PickupDirectory. Created if missing.',      N'email', N'String',  N'D:\ICBC - Latest\MailDrop',  N'D:\ICBC - Latest\MailDrop',  NULL, NULL,                                                        1, 0, 0, 0, 30),
('Email.Host',            N'SMTP host',          N'SMTP server hostname. Used only in Smtp delivery mode.',                                                    N'email', N'String',  N'',                           N'',                           NULL, NULL,                                                        1, 0, 0, 0, 40),
('Email.Port',            N'SMTP port',          N'SMTP server port. Used only in Smtp delivery mode.',                                                        N'email', N'Integer', N'587',                        N'587',                        NULL, N'{"min":1,"max":65535,"step":1}',                            1, 0, 0, 0, 50),
('Email.Username',        N'SMTP username',      N'SMTP auth username. Used only in Smtp delivery mode. (The SMTP password is a file secret, not managed here.)', N'email', N'String', N'',                          N'',                           NULL, NULL,                                                        1, 0, 0, 0, 60),
('Email.UseSsl',          N'Use SSL/TLS',        N'Enable SSL/TLS when connecting to the SMTP server. Used only in Smtp delivery mode.',                       N'email', N'Boolean', N'true',                       N'true',                       NULL, NULL,                                                        1, 0, 0, 0, 70),
('Email.From',            N'From address',       N'Sender address used on outbound notification emails.',                                                      N'email', N'String',  N'notifications@icbc.local',   N'notifications@icbc.local',   NULL, NULL,                                                        1, 0, 0, 0, 80),
('Email.MinSeverity',     N'Minimum severity',   N'Only notifications at or above this severity are emailed.',                                                  N'email', N'Enum',    N'critical',                   N'critical',                   NULL, N'{"allowedValues":["info","warning","critical"]}',           1, 0, 0, 0, 90);

-- insert new definitions (skip any Configuration_Key that already exists)
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, d.Descr, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @D d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - email definitions inserted: ', @@ROWCOUNT);

-- insert the Global active value for each
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Val, 1, @who, @now, @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Section 2 - email Global values inserted: ', @@ROWCOUNT);

-- seed a Create history row per new email definition (audit trail baseline)
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - email Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_07_046 complete - Email / SMTP category + definitions + Global values seeded.';
GO
