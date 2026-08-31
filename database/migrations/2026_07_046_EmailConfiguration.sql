-- =========================================================================
-- 2026_07_046_EmailConfiguration.sql
-- -------------------------------------------------------------------------
-- Email / SMTP settings for the Configuration Management module. Adds an
-- 'email' category + 9 definitions and seeds their Global values + a Create
-- history row each.
--
-- *** VALUES ARE DECLARED, AND MANDATORY (Section 2b) ***  Every non-secret
--   value is declared once in @VAL and edited per environment (<< ENV >> tags
--   mark host / port / from / pickup path). The migration ABORTS if any is not
--   declared. A declared value MAY be empty (e.g. Host/Username when in
--   PickupDirectory mode) — presence is what's required.
--
-- SECRET stays out of the DB bridge: the SMTP PASSWORD (Email.Password) is NOT
-- declared here — it is set encrypted from the Configuration UI.
-- GLOBAL only. Idempotent.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_046_EmailConfiguration.sql"
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

-- 2a. CATALOGUE METADATA (fixed).
DECLARE @DEF TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @DEF (K,Name,Descr,Cat,Dt,Def,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Email.Enabled',         N'Email enabled',      N'Master on/off for the notification email channel. When off, email sending is a no-op.',                    N'email', N'Boolean', N'true',                       NULL, NULL,                                           1, 0, 0, 0, 10),
('Email.DeliveryMode',    N'Delivery mode',      N'Transport: PickupDirectory writes .eml files to the pickup folder (no SMTP); Smtp sends over the network.', N'email', N'Enum',    N'PickupDirectory',            NULL, N'{"allowedValues":["PickupDirectory","Smtp"]}', 1, 0, 0, 0, 20),
('Email.PickupDirectory', N'Pickup directory',   N'Folder that receives generated .eml files when delivery mode is PickupDirectory. Created if missing.',      N'email', N'String',  N'D:\ICBC - Latest\MailDrop',  NULL, NULL,                                           1, 0, 0, 0, 30),
('Email.Host',            N'SMTP host',          N'SMTP server hostname. Used only in Smtp delivery mode.',                                                    N'email', N'String',  N'',                           NULL, NULL,                                           1, 0, 0, 0, 40),
('Email.Port',            N'SMTP port',          N'SMTP server port. Used only in Smtp delivery mode.',                                                        N'email', N'Integer', N'587',                        NULL, N'{"min":1,"max":65535,"step":1}',               1, 0, 0, 0, 50),
('Email.Username',        N'SMTP username',      N'SMTP auth username. Used only in Smtp delivery mode. (The SMTP password is a secret, set in the UI.)',      N'email', N'String',  N'',                           NULL, NULL,                                           1, 0, 0, 0, 60),
('Email.UseSsl',          N'Use SSL/TLS',        N'Enable SSL/TLS when connecting to the SMTP server. Used only in Smtp delivery mode.',                       N'email', N'Boolean', N'true',                       NULL, NULL,                                           1, 0, 0, 0, 70),
('Email.From',            N'From address',       N'Sender address used on outbound notification emails.',                                                      N'email', N'String',  N'notifications@icbc.local',   NULL, NULL,                                           1, 0, 0, 0, 80),
('Email.MinSeverity',     N'Minimum severity',   N'Only notifications at or above this severity are emailed.',                                                  N'email', N'Enum',    N'critical',                   NULL, N'{"allowedValues":["info","warning","critical"]}', 1, 0, 0, 0, 90);

-- 2b. ENVIRONMENT VALUES *** REQUIRED — declare every non-secret value ***
DECLARE @VAL TABLE (K nvarchar(200) PRIMARY KEY, Val nvarchar(max) NULL);
INSERT INTO @VAL (K, Val) VALUES
('Email.Enabled',         N'true'),
('Email.DeliveryMode',    N'PickupDirectory'),
('Email.PickupDirectory', N'D:\ICBC - Latest\MailDrop'),   -- << ENV >>  UAT pickup folder
('Email.Host',            N''),                             -- << ENV >>  UAT SMTP host (blank in PickupDirectory mode)
('Email.Port',            N'587'),                          -- << ENV >>
('Email.Username',        N''),                             -- << ENV >>  UAT SMTP user (blank in PickupDirectory mode)
('Email.UseSsl',          N'true'),
('Email.From',            N'notifications@icbc.local'),     -- << ENV >>  UAT sender
('Email.MinSeverity',     N'critical');

-- 2c. COMPLETENESS GUARD (presence-based; empty allowed but surfaced)
IF EXISTS (SELECT 1 FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K))
BEGIN
    PRINT '!! Declared value(s) reference an UNKNOWN setting key:';
    SELECT v.K AS Unknown_Value_Key FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K);
    RAISERROR('2026_07_046 aborted: unknown key in the value declaration. Nothing seeded.', 16, 1);
    RETURN;
END;
IF EXISTS (SELECT 1 FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K))
BEGIN
    PRINT '!! These non-secret settings are NOT DECLARED — every value MUST be declared:';
    SELECT d.Cat AS Category, d.K AS Undeclared_Setting_Key
    FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K)
    ORDER BY d.Cat, d.K;
    RAISERROR('2026_07_046 aborted: a non-secret setting is not declared. Nothing seeded.', 16, 1);
    RETURN;
END;
IF EXISTS (SELECT 1 FROM @VAL v JOIN @DEF d ON d.K = v.K WHERE d.Sensitive = 0 AND (v.Val IS NULL OR LTRIM(RTRIM(v.Val)) = N''))
BEGIN
    PRINT '(i) NOTE - settings declared with an EMPTY value (confirm intentional):';
    SELECT d.Cat AS Category, v.K AS Empty_Value_Key
    FROM @VAL v JOIN @DEF d ON d.K = v.K WHERE d.Sensitive = 0 AND (v.Val IS NULL OR LTRIM(RTRIM(v.Val)) = N'')
    ORDER BY d.Cat, v.K;
END;
PRINT 'Section 2 - value declaration complete: every non-secret setting is declared.';

-- insert definitions (metadata)
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, d.Descr, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @DEF d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - email definitions inserted: ', @@ROWCOUNT);

-- insert the Global value from @VAL
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, v.Val, 1, @who, @now, @who, @now
FROM   @VAL v
JOIN   @DEF d   ON d.K = v.K
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = v.K
WHERE  d.Sensitive = 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value cv
                   WHERE cv.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND cv.Branch_ID IS NULL AND cv.Role_ID IS NULL AND cv.Environment IS NULL);
PRINT CONCAT('Section 2 - email Global values inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @DEF d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - email Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_07_046 complete - Email / SMTP category + declared values seeded.';
GO
