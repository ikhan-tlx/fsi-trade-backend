-- =========================================================================
-- 2026_07_041_RetireLegacyOnboardingFields.sql
-- -------------------------------------------------------------------------
-- Cleanup: the dynamic Provider Profile (Onboarding.ProviderProfile) is now
-- the single source of truth for the KYC provider. Two earlier flat fields
-- are fully superseded and must no longer appear in the Configuration UI or
-- resolve via the DB->IConfiguration bridge:
--
--   * Onboarding.BaseUrl        -> replaced by the profile's active-environment
--                                  baseUrl (shown/edited in the Builder's
--                                  Environments tab; used by the engine + test).
--   * Onboarding.ResponseMapping-> replaced by the profile's responseMapping
--                                  (JSONPath + value maps + default + type).
--
-- We DEACTIVATE (soft-retire) the definition and its value rows — history is
-- preserved, nothing is hard-deleted. Idempotent.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_041_RetireLegacyOnboardingFields.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_07_041)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

DECLARE @keys TABLE (k nvarchar(200));
INSERT INTO @keys (k) VALUES (N'Onboarding.BaseUrl'), (N'Onboarding.ResponseMapping');

-- Deactivate the active value rows (removes them from the config bridge).
UPDATE v
   SET v.Is_Active = 0, v.Last_Updated_By = @who, v.Last_Updated_Date = @now
  FROM dbo.TmX_Configuration_Value v
  JOIN dbo.TmX_Configuration_Definition d ON d.Configuration_Definition_ID = v.Configuration_Definition_ID
 WHERE d.Configuration_Key IN (SELECT k FROM @keys) AND v.Is_Active = 1;

-- Deactivate the definitions (removes them from the Configuration catalogue/UI).
UPDATE d
   SET d.Is_Active = 0, d.Last_Updated_By = @who, d.Last_Updated_Date = @now
  FROM dbo.TmX_Configuration_Definition d
 WHERE d.Configuration_Key IN (SELECT k FROM @keys) AND d.Is_Active = 1;

-- Audit trail.
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT d.Configuration_Definition_ID, NULL, NULL, N'(retired — superseded by Onboarding.ProviderProfile)',
       'Deactivate', 'Global', @who, @now
  FROM dbo.TmX_Configuration_Definition d
 WHERE d.Configuration_Key IN (SELECT k FROM @keys);

PRINT '2026_07_041 complete - retired Onboarding.BaseUrl + Onboarding.ResponseMapping.';
GO
