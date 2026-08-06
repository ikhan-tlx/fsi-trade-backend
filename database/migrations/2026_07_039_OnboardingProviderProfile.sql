-- =========================================================================
-- 2026_07_039_OnboardingProviderProfile.sql
-- -------------------------------------------------------------------------
-- Dynamic Third-Party KYC integration: seed the Provider Profile config value
-- (Onboarding.ProviderProfile, Json). The DynamicKycScreeningService binds this
-- profile from the DB->IConfiguration bridge and executes it — so onboarding a
-- new provider is a PROFILE EDIT, no code change. Default = the BRAINS-compatible
-- GET shape pointing at the local test provider. See
-- KYC_PROVIDER_DYNAMIC_INTEGRATION_DESIGN.md.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_039_OnboardingProviderProfile.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_07_039)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();
DECLARE @profile nvarchar(max) = N'{
  "name": "Default (BRAINS-compatible)",
  "activeEnvironment": "Development",
  "environments": {
    "Development": { "baseUrl": "http://localhost:9099/QueryKycInfoServlet", "auth": { "type": "None" } },
    "UAT":         { "baseUrl": "", "auth": { "type": "None" } },
    "Production":  { "baseUrl": "", "auth": { "type": "None" } }
  },
  "request": {
    "method": "GET",
    "query": { "APPLY_NO": "001", "TASK_NO": "{{Now}}", "ZONENO": "6001", "ADJUST_TYPE": "1", "CUSTNO": "{{CustomerCode}}", "REG_TYPE": "", "REG_NO": "" }
  },
  "auth": { "type": "None" },
  "success": { "statusRule": "2xx", "jsonConditions": [ { "path": "$.TRANSOK", "op": "eq", "value": "0" } ] },
  "responseMapping": {
    "riskLevel":    { "path": "$.STATUS", "splitOn": "|", "splitIndex": 0, "type": "string", "default": "Unknown", "valueMap": { "0": "PEP", "1": "High", "2": "Medium", "3": "Low" } },
    "customerName": { "path": "$.STATUS", "splitOn": "|", "splitIndex": 1, "type": "string" }
  }
}';

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Onboarding.ProviderProfile')
BEGIN
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        ('Onboarding.ProviderProfile', N'Provider profile (dynamic integration)',
         N'Full data-driven definition of the KYC provider: environments, request (method/URL/headers/auth/query/body template with {{placeholders}}), success criteria, and JSONPath response mapping with value transforms. Edit this to onboard a new provider — no code change.',
         'onboarding', 'Json', @profile, NULL, NULL, 1, 0, 0, 0, 1, 6, @who, @now, @who, @now);

    DECLARE @defId int = (SELECT Configuration_Definition_ID FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Onboarding.ProviderProfile');
    INSERT INTO dbo.TmX_Configuration_Value
        (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES (@defId, @profile, 1, @who, @now, @who, @now);
    INSERT INTO dbo.TmX_Configuration_History
        (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
    SELECT @defId, (SELECT TOP 1 Configuration_Value_ID FROM dbo.TmX_Configuration_Value WHERE Configuration_Definition_ID=@defId), NULL, @profile, 'Create', 'Global', @who, @now;
    PRINT 'Onboarding.ProviderProfile seeded.';
END
ELSE
    PRINT 'Onboarding.ProviderProfile already exists - skipped.';
GO

PRINT '2026_07_039 complete - Onboarding.ProviderProfile.';
GO
