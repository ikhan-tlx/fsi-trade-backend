-- =========================================================================
-- 2026_07_036_ConfigurationManagement.sql
-- -------------------------------------------------------------------------
-- Configuration Management module (/admin/configuration) — Phase 1.
-- See CONFIGURATION_MANAGEMENT_MODULE_DESIGN_AND_PLAN.md (§3 schema) and
-- Design Html/CONFIGURATION_MANAGEMENT_IMPLEMENTATION_NOTES.md.
--
-- Creates the DB-driven settings store (Category + Definition + Value +
-- History), seeds the catalogue + Global values reconciled against the REAL
-- appsettings.json, and seeds Configuration.View/.Manage privileges + the
-- IT Admin grant.
--
-- KEY DESIGN NOTE (the IOptions bridge): a setting's Configuration_Key is the
-- dotted appsettings path (e.g. 'PeriodicReview.BatchSize'). The
-- DbConfigurationProvider republishes each NON-sensitive, NON-logging value
-- into IConfiguration under that path (dots -> ':'), so every existing
-- IOptions/IOptionsMonitor<T> consumer reads the DB value with hot-reload and
-- ZERO call-site change. Secrets stay in appsettings (Is_Sensitive=1, not
-- bridged). Logging.* + Onboarding.* keys are stored/managed here but bridged
-- to real consumers in Phase 2 (see the summary doc); they are NOT wired to
-- business logic in Phase 1.
--
-- Idempotent: every object guarded by IF NOT EXISTS / OBJECT_ID; seed rows
-- guarded by NOT EXISTS on the natural key. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_036_ConfigurationManagement.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Tables ----------
IF OBJECT_ID('dbo.TmX_Configuration_Category','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Configuration_Category (
        Category_Key   nvarchar(50)  NOT NULL CONSTRAINT PK_TmX_Configuration_Category PRIMARY KEY,
        Title          nvarchar(120) NOT NULL,
        Description     nvarchar(400) NULL,
        Display_Order   int           NOT NULL CONSTRAINT DF_TmX_Cfg_Cat_Order DEFAULT(0),
        Verify_Kind     nvarchar(20)  NULL,   -- e.g. 'fccm' -> shows Test-connection chip
        Is_Flagged      bit           NOT NULL CONSTRAINT DF_TmX_Cfg_Cat_Flagged DEFAULT(0),
        Is_Logging      bit           NOT NULL CONSTRAINT DF_TmX_Cfg_Cat_Logging DEFAULT(0)
    );
    PRINT 'Created dbo.TmX_Configuration_Category.';
END
GO

IF OBJECT_ID('dbo.TmX_Configuration_Definition','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Configuration_Definition (
        Configuration_Definition_ID int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Configuration_Definition PRIMARY KEY,
        Configuration_Key   nvarchar(200) NOT NULL,          -- dotted appsettings path
        Display_Name        nvarchar(200) NOT NULL,
        Description         nvarchar(1000) NULL,
        Category_Key        nvarchar(50)  NOT NULL,
        Data_Type           nvarchar(20)  NOT NULL,          -- Boolean|Integer|Decimal|String|Text|Enum|Json|TimeSpan|DateTime|Secret|LogModule
        Default_Value       nvarchar(max) NULL,
        Validation_Rules    nvarchar(max) NULL,              -- JSON: {min,max,step,allowedValues,regex,required}
        Unit                nvarchar(40)  NULL,
        Is_Editable         bit NOT NULL CONSTRAINT DF_TmX_Cfg_Def_Editable  DEFAULT(1),
        Is_Sensitive        bit NOT NULL CONSTRAINT DF_TmX_Cfg_Def_Sensitive DEFAULT(0),
        Requires_Approval   bit NOT NULL CONSTRAINT DF_TmX_Cfg_Def_Approval  DEFAULT(0),
        Applies_On_Restart  bit NOT NULL CONSTRAINT DF_TmX_Cfg_Def_Restart   DEFAULT(0),
        Is_Active           bit NOT NULL CONSTRAINT DF_TmX_Cfg_Def_Active     DEFAULT(1),
        Display_Order       int NOT NULL CONSTRAINT DF_TmX_Cfg_Def_Order      DEFAULT(0),
        Created_By          nvarchar(100) NULL,
        Created_Date        datetime2(3)  NULL,
        Last_Updated_By     nvarchar(100) NULL,
        Last_Updated_Date   datetime2(3)  NULL,
        CONSTRAINT UX_TmX_Cfg_Def_Key UNIQUE (Configuration_Key)
    );
    PRINT 'Created dbo.TmX_Configuration_Definition.';
END
GO

IF OBJECT_ID('dbo.TmX_Configuration_Value','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Configuration_Value (
        Configuration_Value_ID     int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Configuration_Value PRIMARY KEY,
        Configuration_Definition_ID int NOT NULL
            CONSTRAINT FK_TmX_Cfg_Value_Def REFERENCES dbo.TmX_Configuration_Definition(Configuration_Definition_ID),
        Configuration_Value nvarchar(max) NULL,   -- NULL => use Default_Value
        Branch_ID           int NULL,             -- future scope (NULL = Global)
        Role_ID             int NULL,             -- future scope
        Environment         nvarchar(40) NULL,    -- future scope
        Pending_Value       nvarchar(max) NULL,   -- dual-control: awaiting approval
        Pending_By          nvarchar(100) NULL,
        Pending_Date        datetime2(3)  NULL,
        Is_Active           bit NOT NULL CONSTRAINT DF_TmX_Cfg_Val_Active DEFAULT(1),
        Created_By          nvarchar(100) NULL,
        Created_Date        datetime2(3)  NULL,
        Last_Updated_By     nvarchar(100) NULL,
        Last_Updated_Date   datetime2(3)  NULL
    );
    CREATE UNIQUE INDEX UX_TmX_Cfg_Value_Scope
        ON dbo.TmX_Configuration_Value (Configuration_Definition_ID, Branch_ID, Role_ID, Environment)
        WHERE Is_Active = 1;
    PRINT 'Created dbo.TmX_Configuration_Value.';
END
GO

IF OBJECT_ID('dbo.TmX_Configuration_History','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Configuration_History (
        Configuration_History_ID    bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Configuration_History PRIMARY KEY,
        Configuration_Definition_ID int NOT NULL
            CONSTRAINT FK_TmX_Cfg_Hist_Def REFERENCES dbo.TmX_Configuration_Definition(Configuration_Definition_ID),
        Configuration_Value_ID int NULL,
        Old_Value       nvarchar(max) NULL,
        New_Value       nvarchar(max) NULL,
        Change_Type     nvarchar(20)  NOT NULL,   -- Update|Reset|Create|Rollback|Deactivate|SubmitForApproval
        Scope_Descriptor nvarchar(200) NULL,
        Request_ID      bigint NULL,
        Changed_By      nvarchar(100) NULL,
        Changed_Date    datetime2(3)  NOT NULL CONSTRAINT DF_TmX_Cfg_Hist_Date DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_TmX_Cfg_Hist_Def ON dbo.TmX_Configuration_History (Configuration_Definition_ID, Changed_Date DESC);
    PRINT 'Created dbo.TmX_Configuration_History.';
END
GO

-- ---------- 2. Seed categories ----------
MERGE dbo.TmX_Configuration_Category AS tgt
USING (VALUES
    ('security', N'Security / Session',               N'Tokens, sessions and second factor',            10, NULL,   0, 0),
    ('password', N'Password Policy',                   N'Lockout, expiry and dormancy',                  20, NULL,   0, 0),
    ('review',   N'Periodic Review',                   N'Scheduled re-review of onboarded customers',    30, NULL,   0, 0),
    ('insight',  N'Customer Insight',                  N'Screening refresh for existing customers',      40, NULL,   0, 0),
    ('repo',     N'Trade Repository',                  N'Transaction handling thresholds',               50, NULL,   0, 0),
    ('fccm',     N'FCCM / Integration',                N'Oracle FCCM case handoff',                      60, 'fccm', 0, 0),
    ('onboarding',N'Onboarding / External Verification',N'Third-party verification at KYC creation',     70, NULL,   1, 0),
    ('reports',  N'Reports / Documents',               N'Storage paths and rendering',                   80, NULL,   0, 0),
    ('notify',   N'Notifications',                     N'Channels and templates',                        90, NULL,   0, 0),
    ('logging',  N'Diagnostics / Logging',             N'Log levels, sinks and retention',              100, NULL,   0, 1)
) AS src (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
ON tgt.Category_Key = src.Category_Key
WHEN NOT MATCHED THEN
    INSERT (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
    VALUES (src.Category_Key, src.Title, src.Description, src.Display_Order, src.Verify_Kind, src.Is_Flagged, src.Is_Logging);
PRINT CONCAT('Section 2 - categories upserted: ', @@ROWCOUNT);
GO

-- ---------- 3. Seed definitions + Global values ----------
DECLARE @who nvarchar(100) = N'System (2026_07_036)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- staging: one row per setting (Value = the current Global value = the real appsettings value)
DECLARE @D TABLE (
    K nvarchar(200), Name nvarchar(200), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Val nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @D (K,Name,Cat,Dt,Def,Val,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
-- security
('Jwt.AccessTokenDefaultMinutes',N'Access token lifetime','security','Integer',N'1440',N'1440',N'minutes',N'{"min":5,"max":480,"step":5}',1,0,1,0,10),
('Auth.RestrictConcurrentLogin',N'Restrict concurrent login','security','Boolean',N'false',N'false',NULL,NULL,1,0,0,0,20),
('Auth.RequireDeviceIdHeader',N'Require device-id header','security','Boolean',N'true',N'true',NULL,NULL,1,0,0,0,30),
('TwoFactor.Enabled',N'Two-factor authentication','security','Boolean',N'true',N'true',NULL,NULL,1,0,0,0,40),
('TwoFactor.Method',N'Two-factor method','security','Enum',N'Totp',N'Totp',NULL,N'{"allowedValues":["Email","SMS","Totp","None"]}',1,0,0,0,50),
-- password (real section = PasswordPolicy)
('PasswordPolicy.MaxFailedAttempts',N'Max failed attempts','password','Integer',N'5',N'5',N'attempts',N'{"min":1,"max":10,"step":1}',1,0,0,0,10),
('PasswordPolicy.LockoutMinutes',N'Lockout duration','password','Integer',N'3',N'3',N'minutes',N'{"min":1,"max":1440,"step":5}',1,0,0,0,20),
('PasswordPolicy.EnforceExpiry',N'Enforce password expiry','password','Boolean',N'true',N'true',NULL,NULL,1,0,0,0,30),
('PasswordPolicy.ExpiryDays',N'Password expiry','password','Integer',N'90',N'90',N'days',N'{"min":30,"max":365,"step":15}',1,0,1,0,40),
('PasswordPolicy.DormancyDays',N'Dormancy threshold','password','Integer',N'90',N'90',N'days',N'{"min":15,"max":365,"step":5}',1,0,0,0,50),
-- review
('PeriodicReview.Enabled',N'Periodic review','review','Boolean',N'true',N'true',NULL,NULL,1,0,0,0,10),
('PeriodicReview.ScanInterval',N'Scan interval','review','TimeSpan',N'01:00:00',N'01:00:00',NULL,NULL,1,0,0,0,20),
('PeriodicReview.BatchSize',N'Batch size','review','Integer',N'50',N'50',N'records',N'{"min":1,"max":500,"step":10}',1,0,0,0,30),
('PeriodicReview.LeadTimeDays',N'Lead time','review','Integer',N'0',N'0',N'days',N'{"min":0,"max":90,"step":1}',1,0,0,0,40),
('PeriodicReview.DefaultFrequency',N'Default review frequency','review','Json',N'{"Value":3,"Unit":"Months"}',N'{"Value":3,"Unit":"Months"}',NULL,NULL,1,0,0,0,50),
('PeriodicReview.Frequencies',N'Frequency by risk band','review','Json',N'{"PEP":{"Value":1,"Unit":"Months"},"High":{"Value":1,"Unit":"Months"},"Medium":{"Value":2,"Unit":"Months"},"Low":{"Value":3,"Unit":"Months"}}',N'{"PEP":{"Value":1,"Unit":"Months"},"High":{"Value":1,"Unit":"Months"},"Medium":{"Value":2,"Unit":"Months"},"Low":{"Value":3,"Unit":"Months"}}',NULL,NULL,1,0,0,0,60),
-- insight (real section = CustomerInsightSnapshot)
('CustomerInsightSnapshot.Enabled',N'Customer insight service','insight','Boolean',N'true',N'true',NULL,NULL,1,0,0,0,10),
('CustomerInsightSnapshot.CheckInterval',N'Check interval','insight','TimeSpan',N'06:00:00',N'06:00:00',NULL,NULL,1,0,0,0,20),
-- repo
('TradeRepository.SlaDays',N'Review SLA','repo','Integer',N'5',N'5',N'days',N'{"min":1,"max":30,"step":1}',1,0,0,0,10),
-- fccm / integration
('Integration.FccmOracleConnection',N'FCCM database connection','fccm','Secret',N'',NULL,NULL,NULL,0,1,0,0,10),
('Integration.TradeCaseDefaults.Jurisdiction',N'Default jurisdiction','fccm','String',N'All',N'All',NULL,NULL,1,0,0,0,20),
('Integration.TradeCaseDefaults.CaseType',N'Default case type','fccm','Enum',N'TBML_SURV',N'TBML_SURV',NULL,N'{"allowedValues":["TBML_SURV","TF_SURV","SANC_SCR"]}',1,0,0,0,30),
('Integration.PollingInterval',N'Polling interval','fccm','TimeSpan',N'00:00:05',N'00:00:05',NULL,NULL,1,0,0,0,40),
('Integration.CaseIdTimeout',N'Case-id timeout','fccm','TimeSpan',N'00:05:00',N'00:05:00',NULL,NULL,1,0,0,0,50),
('Integration.HttpTimeout',N'HTTP timeout','fccm','TimeSpan',N'00:00:30',N'00:00:30',NULL,NULL,1,0,0,0,55),
('Integration.FccmOnboardingUrl',N'FCCM onboarding URL','fccm','String',N'http://172.20.3.73:7001/InitiateOnboardingService/OB/Initiate',N'http://172.20.3.73:7001/InitiateOnboardingService/OB/Initiate',NULL,N'{"format":"url"}',1,0,0,0,60),
('Integration.FccmActionCodeStatusMap',N'Action-code status map','fccm','Json',N'{"30004":"APPROVED","30003":"REJECTED","983":"APPROVED","984":"REJECTED"}',N'{"30004":"APPROVED","30003":"REJECTED","983":"APPROVED","984":"REJECTED"}',NULL,NULL,1,0,0,0,70),
-- onboarding / external verification (NEW — Phase 2 consumers; not bridged in Phase 1)
('Onboarding.RequireExternalVerification',N'Require external verification for KYC','onboarding','Boolean',N'false',N'false',NULL,NULL,1,0,0,0,10),
('Onboarding.BaseUrl',N'Base URL','onboarding','String',N'',N'http://localhost:8080/icbc/brainsEU/servlet/QueryKycInfoServlet',NULL,N'{"format":"url"}',1,0,0,0,20),
('Onboarding.ResponseMapping',N'Response mapping','onboarding','Json',N'{"riskField":"STATUS","map":{"0":"PEP","1":"High","2":"Medium","3":"Low"},"successFlag":{"TRANSOK":"0"}}',N'{"riskField":"STATUS","map":{"0":"PEP","1":"High","2":"Medium","3":"Low"},"successFlag":{"TRANSOK":"0"}}',NULL,NULL,1,0,0,0,30),
('Onboarding.FailureHandling',N'Failure handling','onboarding','Enum',N'Block',N'Block',NULL,N'{"allowedValues":["Block","Allow with flag","Allow with warning"]}',1,0,0,0,40),
('Onboarding.CallTimeout',N'Call timeout','onboarding','TimeSpan',N'00:00:30',N'00:00:30',NULL,NULL,1,0,0,0,50),
-- reports / documents
('Documents.BasePath',N'Document storage path','reports','String',N'ICBC_Data',N'ICBC_Data',NULL,NULL,0,0,0,0,10),
('Reports.ChromiumExecutablePath',N'Chromium executable path','reports','String',N'',N'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',NULL,NULL,1,0,0,0,20),
('Reports.FooterDisclaimer',N'Report footer disclaimer','reports','Text',N'Internal use only.',N'This report is generated from ICBC Trade Compliance records and is intended for internal compliance use only.',NULL,NULL,1,0,0,0,30),
-- logging / diagnostics (managed here; Phase 2 wires the LoggingLevelSwitch. directory/retention are restart-scoped)
('Logging.Modules.Api',N'Api','logging','LogModule',N'{"enabled":true,"level":"Information"}',N'{"enabled":true,"level":"Information"}',NULL,NULL,1,0,0,0,10),
('Logging.Modules.Application.Trade',N'Application.Trade','logging','LogModule',N'{"enabled":true,"level":"Information"}',N'{"enabled":true,"level":"Debug"}',NULL,NULL,1,0,0,0,20),
('Logging.Modules.Application.UserManagement',N'Application.UserManagement','logging','LogModule',N'{"enabled":true,"level":"Information"}',N'{"enabled":true,"level":"Information"}',NULL,NULL,1,0,0,0,30),
('Logging.Modules.BackgroundService',N'BackgroundService','logging','LogModule',N'{"enabled":true,"level":"Information"}',N'{"enabled":true,"level":"Information"}',NULL,NULL,1,0,0,0,40),
('Logging.Directory',N'Log directory','logging','String',N'D:\ICBC - Latest\Logs',N'D:\ICBC - Latest\Logs',NULL,NULL,1,0,0,1,50),
('Logging.RetentionDays',N'Retention','logging','Integer',N'30',N'30',N'days',N'{"min":1,"max":365,"step":1}',1,0,0,1,60),
('Logging.ArchiveScanIntervalHours',N'Archive scan interval','logging','Integer',N'12',N'12',N'hours',N'{"min":1,"max":72,"step":1}',1,0,0,1,70);

-- descriptions kept in a second pass for readability (optional; safe if absent)
-- insert new definitions
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, NULL, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @D d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 3 - definitions inserted: ', @@ROWCOUNT);

-- insert the Global active value for each (skip sensitive: value stays in appsettings)
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Val, 1, @who, @now, @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  d.Sensitive = 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Section 3 - Global values inserted: ', @@ROWCOUNT);

-- seed a Create history row per new definition (audit trail baseline)
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   dbo.TmX_Configuration_Definition def
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 3 - Create history rows: ', @@ROWCOUNT);
GO

-- ---------- 4. Privileges + IT Admin grant ----------
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Privilege ORDER BY Privilege_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @now datetime = GETUTCDATE();
DECLARE @far datetime = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_07_036)';

DECLARE @new TABLE (Code nvarchar(100) PRIMARY KEY, Descr nvarchar(200));
INSERT @new(Code,Descr) VALUES
    (N'Configuration.View',   N'View application configuration settings and their history'),
    (N'Configuration.Manage', N'Edit, reset, roll back, reveal secrets, verify integrations, toggle logging');

INSERT INTO dbo.TmX_Privilege
    (Tenant_ID, Privilege_Name, Privilege_Description, Active_Flag, Effective_Start_Date, Effective_End_Date,
     Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT @tenantId, s.Code, s.Descr, 1, @now, @far, @who, @now, @who, @now
FROM   @new s
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Privilege p WHERE p.Privilege_Name = s.Code);
PRINT CONCAT('Section 4 - Configuration privilege rows inserted: ', @@ROWCOUNT);

DECLARE @itAdminRoleId int = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = N'IT Admin');
IF @itAdminRoleId IS NULL
    PRINT '!! Role "IT Admin" not found - grant Configuration.View/.Manage via role management.';
ELSE
BEGIN
    INSERT INTO dbo.TmX_Role_Privilege_Mapping
        (Tenant_ID, Role_ID, Privilege_ID, Active_Flag, Effective_Start_Date, Effective_End_Date,
         Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    SELECT @tenantId, @itAdminRoleId, p.Privilege_ID, 1, @now, @far, @who, @now, NULL, NULL
    FROM   dbo.TmX_Privilege p
    WHERE  p.Privilege_Name IN (N'Configuration.View', N'Configuration.Manage')
      AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Role_Privilege_Mapping rpm
                       WHERE rpm.Role_ID = @itAdminRoleId AND rpm.Privilege_ID = p.Privilege_ID);
    PRINT CONCAT('Section 4 - Configuration grants to IT Admin inserted: ', @@ROWCOUNT);
END
GO

PRINT '2026_07_036 complete - Configuration Management schema + catalogue + privileges seeded.';
GO
