-- =========================================================================
-- 2026_08_061_OnboardingMappingProfile.sql
-- -------------------------------------------------------------------------
-- Surface the FCCM onboarding payload-version selector (v1 / v2) in
-- Configuration Management. It was previously ONLY in appsettings
-- (Integration:Onboarding:MappingProfile) with no UI, so operators couldn't
-- see or switch which onboarding contract the KYC→FCCM submission builds.
--
-- The engine (OnboardingMappingProvider) loads onboarding.{profile}.mapping.json:
--   v1 = flat legacy KYCRequestModel shape · v2 = rich nested OB v2 shape.
--
-- Seeded as an Enum with allowed ["v1","v2"] → the metadata-driven config
-- screen renders a dropdown automatically (no FE change). Default_Value = 'v2'
-- MATCHES the current appsettings value, so bridging this into IConfiguration
-- does NOT change runtime behaviour. appsettings keeps 'v2' as the fail-soft
-- floor for the DB-unavailable case.
--
-- Requires_Approval = 1: flipping the profile changes the entire FCCM payload
-- contract, so the change is maker/checker-gated (files a Configuration Change
-- request, type 0011). Applies_On_Restart = 1: FccmOnboardingOptions is bound
-- via IOptions (read once) + an in-memory profile cache, so a switch takes
-- effect on the next app restart.
--
-- Idempotent: guarded by NOT EXISTS on Configuration_Key. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_061_OnboardingMappingProfile.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_08_061)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = N'Integration.Onboarding.MappingProfile')
BEGIN
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Help_Detail, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Section, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        (N'Integration.Onboarding.MappingProfile',
         N'Onboarding payload version',
         N'Which FCCM onboarding contract the KYC→FCCM submission builds: v1 (legacy flat shape) or v2 (current nested OB v2 shape).',
         N'Selects the mapping profile file onboarding.{profile}.mapping.json loaded by OnboardingMappingProvider for the InitiateOnboarding submission. v1 = flat legacy KYCRequestModel; v2 = rich nested OB v2 payload this environment uses. Approval-gated (changes the whole FCCM payload contract) and applies on the next app restart (options are cached).',
         N'fccm', N'Enum', N'v2',
         N'{"allowedValues":["v1","v2"]}', NULL, 1, 0, 1, 1,
         1, 13, N'Onboarding submission', @who, @now, @who, @now);
    PRINT 'Integration.Onboarding.MappingProfile seeded (Enum v1/v2, default v2, approval-gated).';
END
ELSE
    PRINT 'Integration.Onboarding.MappingProfile already exists - skipped.';
GO

-- Repair (idempotent): the enum dropdown reads Validation_Rules."allowedValues".
-- Force the canonical shape in case an earlier run seeded a different key.
UPDATE dbo.TmX_Configuration_Definition
   SET Validation_Rules = N'{"allowedValues":["v1","v2"]}'
 WHERE Configuration_Key = N'Integration.Onboarding.MappingProfile'
   AND (Validation_Rules IS NULL OR Validation_Rules <> N'{"allowedValues":["v1","v2"]}');
GO

PRINT '2026_08_061 complete - onboarding v1/v2 selector surfaced in Configuration.';
GO
