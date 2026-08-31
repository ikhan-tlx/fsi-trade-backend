-- =========================================================================
-- 2026_07_037_ConfigurationCompleteness.sql
-- -------------------------------------------------------------------------
-- Configuration Management — audit follow-up. Brings previously-unmanaged
-- appsettings keys that SHADOWED or GATED managed settings into the module,
-- completes the FCCM TradeCaseDefaults set, and deactivates the no-consumer
-- Reports.FooterDisclaimer row. See CONFIGURATION_MANAGEMENT_SANITY_AUDIT.md.
--
-- *** VALUES ARE DECLARED, AND MANDATORY (Section 2b) ***
--   Every non-secret setting's Global value is declared once in the @VAL block
--   and edited per environment (<< ENV >> tags mark the ones that usually
--   differ, e.g. UAT OwnerOrg / OwnerId, FCCM Oracle schema, customer-master
--   URL). The migration ABORTS if any non-secret setting is not declared.
--   Metadata (@DEF) is fixed. A declared value MAY be empty (some are
--   legitimately blank); the guard only requires the declaration to be present.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_037_ConfigurationCompleteness.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. New category: Workflow ----------
MERGE dbo.TmX_Configuration_Category AS tgt
USING (VALUES ('workflow', N'Workflow', N'Process engine and inbox', 85, NULL, 0, 0)) AS src
      (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
ON tgt.Category_Key = src.Category_Key
WHEN NOT MATCHED THEN
    INSERT (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
    VALUES (src.Category_Key, src.Title, src.Description, src.Display_Order, src.Verify_Kind, src.Is_Flagged, src.Is_Logging);
PRINT CONCAT('Section 1 - workflow category upserted: ', @@ROWCOUNT);
GO

-- ---------- 2. New definitions + Global values ----------
DECLARE @who nvarchar(100) = N'System (2026_07_037)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- 2a. CATALOGUE METADATA (fixed): key, label, category, type, DEFAULT fallback, unit, rules, flags.
DECLARE @DEF TABLE (
    K nvarchar(200), Name nvarchar(200), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @DEF (K,Name,Cat,Dt,Def,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
-- security
('Jwt.RefreshTokenFactor',N'Refresh token factor','security','Decimal',N'1.5',NULL,N'{"min":1,"max":10}',1,0,0,0,60),
('Jwt.RoleTtlMinutes',N'Per-role token lifetime','security','Json',N'{"Customer":1440,"ServiceUser":1440}',NULL,NULL,1,0,0,0,70),
('Auth.DeviceIdHeaderName',N'Device-id header name','security','String',N'X-Device-Id',NULL,NULL,1,0,0,1,80),
-- password
('PasswordPolicy.EnforceFirstPasswordChange',N'Enforce first-login password change','password','Boolean',N'true',NULL,NULL,1,0,0,0,60),
('PasswordPolicy.EnforceDormancy',N'Enforce dormancy lockout','password','Boolean',N'false',NULL,NULL,1,0,0,0,70),
('PasswordPolicy.DisableExpiryForRoles',N'Roles exempt from expiry','password','Json',N'["Customer"]',NULL,NULL,1,0,0,0,80),
-- fccm
('Integration.TradeCaseDefaults.OwnerOrg',N'Owner org','fccm','String',N'EIB',NULL,NULL,1,0,0,0,100),
('Integration.TradeCaseDefaults.OwnerId',N'Owner id','fccm','Integer',N'10004',NULL,NULL,1,0,0,0,101),
('Integration.TradeCaseDefaults.BusinessDomain',N'Business domain','fccm','String',N'GEN',NULL,NULL,1,0,0,0,102),
('Integration.TradeCaseDefaults.EventType',N'Event type','fccm','String',N'TBAML',NULL,NULL,1,0,0,0,103),
('Integration.TradeCaseDefaults.ScenarioClass',N'Scenario class','fccm','String',N'TBML',NULL,NULL,1,0,0,0,104),
('Integration.TradeCaseDefaults.DataOrigin',N'Data origin','fccm','String',N'DLY',NULL,NULL,1,0,0,0,105),
('Integration.TradeCaseDefaults.EventStatus',N'Event status','fccm','String',N'CLS',NULL,NULL,1,0,0,0,106),
('Integration.TradeCaseDefaults.EventPriority',N'Event priority','fccm','String',N'HIGH',NULL,NULL,1,0,0,0,107),
('Integration.TradeCaseDefaults.CasePriority',N'Case priority','fccm','String',N'High',NULL,NULL,1,0,0,0,108),
('Integration.TradeCaseDefaults.CaseSource',N'Case source','fccm','String',N'CR',NULL,NULL,1,0,0,0,109),
('Integration.TradeCaseDefaults.CaseStatus',N'Case status','fccm','String',N'NW',NULL,NULL,1,0,0,0,110),
('Integration.TradeCaseDefaults.ContractEventType',N'Contract event type','fccm','String',N'ISSU',NULL,NULL,1,0,0,0,111),
('Integration.TradeCaseDefaults.CustomerType',N'Customer type','fccm','String',N'ORG',NULL,NULL,1,0,0,0,112),
('Integration.TradeCaseDefaults.CustomerBusinessDomainList',N'Customer business-domain list','fccm','String',N'abc',NULL,NULL,1,0,0,0,113),
('Integration.CustomerMasterBaseUrl',N'Customer-master base URL','fccm','String',N'',NULL,N'{"format":"url"}',1,0,0,0,120),
('Integration.FccmOracleSchema',N'FCCM Oracle schema','fccm','String',N'',NULL,NULL,1,0,0,0,121),
-- reports
('Reports.ChromiumCachePath',N'Chromium cache path','reports','String',N'',NULL,NULL,1,0,0,1,40),
-- workflow
('Workflow.InboxMaxPageSize',N'Inbox max page size','workflow','Integer',N'100',N'records',N'{"min":1,"max":500,"step":10}',1,0,0,0,10);

-- 2b. ENVIRONMENT VALUES *** REQUIRED — declare every non-secret value ***
DECLARE @VAL TABLE (K nvarchar(200) PRIMARY KEY, Val nvarchar(max) NULL);
INSERT INTO @VAL (K, Val) VALUES
-- security
('Jwt.RefreshTokenFactor',   N'1.5'),
('Jwt.RoleTtlMinutes',       N'{"Customer":1440,"ServiceUser":1440}'),
('Auth.DeviceIdHeaderName',  N'X-Device-Id'),
-- password
('PasswordPolicy.EnforceFirstPasswordChange', N'true'),
('PasswordPolicy.EnforceDormancy',            N'false'),
('PasswordPolicy.DisableExpiryForRoles',      N'["Customer"]'),
-- fccm
('Integration.TradeCaseDefaults.OwnerOrg',              N'EIB'),      -- << ENV >>  UAT owner org
('Integration.TradeCaseDefaults.OwnerId',               N'10004'),    -- << ENV >>  UAT owner id
('Integration.TradeCaseDefaults.BusinessDomain',        N'GEN'),
('Integration.TradeCaseDefaults.EventType',             N'TBAML'),
('Integration.TradeCaseDefaults.ScenarioClass',         N'TBML'),
('Integration.TradeCaseDefaults.DataOrigin',            N'DLY'),
('Integration.TradeCaseDefaults.EventStatus',           N'CLS'),
('Integration.TradeCaseDefaults.EventPriority',         N'HIGH'),
('Integration.TradeCaseDefaults.CasePriority',          N'High'),
('Integration.TradeCaseDefaults.CaseSource',            N'CR'),
('Integration.TradeCaseDefaults.CaseStatus',            N'NW'),
('Integration.TradeCaseDefaults.ContractEventType',     N'ISSU'),
('Integration.TradeCaseDefaults.CustomerType',          N'ORG'),
('Integration.TradeCaseDefaults.CustomerBusinessDomainList', N'abc'),
('Integration.CustomerMasterBaseUrl', N''),   -- << ENV >>  optional; UAT customer-master URL (blank = unset)
('Integration.FccmOracleSchema',      N''),   -- << ENV >>  optional; blank => reader defaults to MANTAS
-- reports
('Reports.ChromiumCachePath', N''),           -- optional; blank => engine default
-- workflow
('Workflow.InboxMaxPageSize', N'100');

-- 2c. COMPLETENESS GUARD (presence-based; empty allowed but surfaced)
IF EXISTS (SELECT 1 FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K))
BEGIN
    PRINT '!! Declared value(s) reference an UNKNOWN setting key:';
    SELECT v.K AS Unknown_Value_Key FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K);
    RAISERROR('2026_07_037 aborted: unknown key in the value declaration. Nothing seeded.', 16, 1);
    RETURN;
END;
IF EXISTS (SELECT 1 FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K))
BEGIN
    PRINT '!! These non-secret settings are NOT DECLARED — every value MUST be declared:';
    SELECT d.Cat AS Category, d.K AS Undeclared_Setting_Key
    FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K)
    ORDER BY d.Cat, d.K;
    RAISERROR('2026_07_037 aborted: a non-secret setting is not declared. Nothing seeded.', 16, 1);
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
SELECT d.K, d.Name, NULL, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @DEF d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - definitions inserted: ', @@ROWCOUNT);

-- insert the Global value (non-secret) from @VAL
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
PRINT CONCAT('Section 2 - Global values inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @DEF d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h
                   WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - Create history rows: ', @@ROWCOUNT);
GO

-- ---------- 3. Deactivate the no-consumer Reports.FooterDisclaimer ----------
UPDATE d SET d.Is_Active = 0, d.Last_Updated_By = N'System (2026_07_037)', d.Last_Updated_Date = SYSUTCDATETIME()
FROM   dbo.TmX_Configuration_Definition d
WHERE  d.Configuration_Key = 'Reports.FooterDisclaimer' AND d.Is_Active = 1;
PRINT CONCAT('Section 3 - Reports.FooterDisclaimer deactivated (no consumer): ', @@ROWCOUNT);
GO

PRINT '2026_07_037 complete - configuration completeness (declared values; shadow/gate keys + FCCM defaults + workflow).';
GO
