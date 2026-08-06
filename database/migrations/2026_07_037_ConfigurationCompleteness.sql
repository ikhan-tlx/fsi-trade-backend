-- =========================================================================
-- 2026_07_037_ConfigurationCompleteness.sql
-- -------------------------------------------------------------------------
-- Configuration Management — audit follow-up. Brings previously-unmanaged
-- appsettings keys that SHADOWED or GATED managed settings into the module,
-- completes the FCCM TradeCaseDefaults set, and deactivates the no-consumer
-- Reports.FooterDisclaimer row. See CONFIGURATION_MANAGEMENT_SANITY_AUDIT.md.
--
-- All values reconciled against the REAL appsettings.json (no behaviour change
-- today — same values, now DB-managed via the IOptions bridge). Idempotent.
--
-- Run:
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

DECLARE @D TABLE (
    K nvarchar(200), Name nvarchar(200), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Val nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @D (K,Name,Cat,Dt,Def,Val,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
-- security (resolve the JWT TTL shadow + expose refresh factor + device header)
('Jwt.RefreshTokenFactor',N'Refresh token factor','security','Decimal',N'1.5',N'1.5',NULL,N'{"min":1,"max":10}',1,0,0,0,60),
('Jwt.RoleTtlMinutes',N'Per-role token lifetime','security','Json',N'{"Customer":1440,"ServiceUser":1440}',N'{"Customer":1440,"ServiceUser":1440}',NULL,NULL,1,0,0,0,70),
('Auth.DeviceIdHeaderName',N'Device-id header name','security','String',N'X-Device-Id',N'X-Device-Id',NULL,NULL,1,0,0,1,80),
-- password (resolve the dormancy gate + expose first-change + role exemptions)
('PasswordPolicy.EnforceFirstPasswordChange',N'Enforce first-login password change','password','Boolean',N'true',N'true',NULL,NULL,1,0,0,0,60),
('PasswordPolicy.EnforceDormancy',N'Enforce dormancy lockout','password','Boolean',N'false',N'false',NULL,NULL,1,0,0,0,70),
('PasswordPolicy.DisableExpiryForRoles',N'Roles exempt from expiry','password','Json',N'["Customer"]',N'["Customer"]',NULL,NULL,1,0,0,0,80),
-- fccm (complete the TradeCaseDefaults set + expose the two integration knobs)
('Integration.TradeCaseDefaults.OwnerOrg',N'Owner org','fccm','String',N'EIB',N'EIB',NULL,NULL,1,0,0,0,100),
('Integration.TradeCaseDefaults.OwnerId',N'Owner id','fccm','Integer',N'10004',N'10004',NULL,NULL,1,0,0,0,101),
('Integration.TradeCaseDefaults.BusinessDomain',N'Business domain','fccm','String',N'GEN',N'GEN',NULL,NULL,1,0,0,0,102),
('Integration.TradeCaseDefaults.EventType',N'Event type','fccm','String',N'TBAML',N'TBAML',NULL,NULL,1,0,0,0,103),
('Integration.TradeCaseDefaults.ScenarioClass',N'Scenario class','fccm','String',N'TBML',N'TBML',NULL,NULL,1,0,0,0,104),
('Integration.TradeCaseDefaults.DataOrigin',N'Data origin','fccm','String',N'DLY',N'DLY',NULL,NULL,1,0,0,0,105),
('Integration.TradeCaseDefaults.EventStatus',N'Event status','fccm','String',N'CLS',N'CLS',NULL,NULL,1,0,0,0,106),
('Integration.TradeCaseDefaults.EventPriority',N'Event priority','fccm','String',N'HIGH',N'HIGH',NULL,NULL,1,0,0,0,107),
('Integration.TradeCaseDefaults.CasePriority',N'Case priority','fccm','String',N'High',N'High',NULL,NULL,1,0,0,0,108),
('Integration.TradeCaseDefaults.CaseSource',N'Case source','fccm','String',N'CR',N'CR',NULL,NULL,1,0,0,0,109),
('Integration.TradeCaseDefaults.CaseStatus',N'Case status','fccm','String',N'NW',N'NW',NULL,NULL,1,0,0,0,110),
('Integration.TradeCaseDefaults.ContractEventType',N'Contract event type','fccm','String',N'ISSU',N'ISSU',NULL,NULL,1,0,0,0,111),
('Integration.TradeCaseDefaults.CustomerType',N'Customer type','fccm','String',N'ORG',N'ORG',NULL,NULL,1,0,0,0,112),
('Integration.TradeCaseDefaults.CustomerBusinessDomainList',N'Customer business-domain list','fccm','String',N'abc',N'abc',NULL,NULL,1,0,0,0,113),
('Integration.CustomerMasterBaseUrl',N'Customer-master base URL','fccm','String',N'',N'',NULL,N'{"format":"url"}',1,0,0,0,120),
('Integration.FccmOracleSchema',N'FCCM Oracle schema','fccm','String',N'',N'',NULL,NULL,1,0,0,0,121),
-- reports
('Reports.ChromiumCachePath',N'Chromium cache path','reports','String',N'',N'',NULL,NULL,1,0,0,1,40),
-- workflow
('Workflow.InboxMaxPageSize',N'Inbox max page size','workflow','Integer',N'100',N'100',N'records',N'{"min":1,"max":500,"step":10}',1,0,0,0,10);

INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, NULL, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @D d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - definitions inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Val, 1, @who, @now, @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  d.Sensitive = 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Section 2 - Global values inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @D d
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

PRINT '2026_07_037 complete - configuration completeness (shadow/gate keys + FCCM defaults + workflow).';
GO
