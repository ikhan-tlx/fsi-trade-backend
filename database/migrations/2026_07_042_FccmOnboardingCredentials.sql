-- =========================================================================
-- 2026_07_042_FccmOnboardingCredentials.sql
-- -------------------------------------------------------------------------
-- Move the FCCM onboarding Basic-auth credentials out of appsettings
-- (Integration:Onboarding:Username / :Password) into the Configuration module
-- (category 'fccm'):
--
--   * Integration.Onboarding.Username  — String, non-sensitive → bridged into
--                                         IConfiguration (Integration:Onboarding:Username),
--                                         so FccmOnboardingOptions binds it as before.
--   * Integration.Onboarding.Password  — Secret, sensitive → NOT bridged; stored
--                                         ENCRYPTED (Data Protection) and read
--                                         decrypted at point of use via
--                                         ISecretConfigReader. Seeded EMPTY here;
--                                         the encrypted value is set from the UI
--                                         (Configuration → FCCM/Integration →
--                                         Update value) which encrypts on write.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_042_FccmOnboardingCredentials.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_07_042)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Integration.Onboarding.Username')
BEGIN
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        ('Integration.Onboarding.Username', N'FCCM onboarding username',
         N'HTTP Basic-auth username for the KYC→FCCM onboarding submission. Managed here (was appsettings Integration:Onboarding:Username).',
         'fccm', 'String', N'KYCADMIN', NULL, NULL, 1, 0, 0, 0, 1, 12, @who, @now, @who, @now);
    PRINT 'Integration.Onboarding.Username seeded.';
END
ELSE
    PRINT 'Integration.Onboarding.Username already exists - skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Integration.Onboarding.Password')
BEGIN
    DECLARE @who2 nvarchar(100) = N'System (2026_07_042)';
    DECLARE @now2 datetime2(3)  = SYSUTCDATETIME();
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        ('Integration.Onboarding.Password', N'FCCM onboarding password',
         N'HTTP Basic-auth password for the KYC→FCCM onboarding submission. Stored ENCRYPTED at rest; set it from Configuration → FCCM/Integration (Update value). Never displayed or logged.',
         'fccm', 'Secret', N'', NULL, NULL, 1, 1, 0, 0, 1, 14, @who2, @now2, @who2, @now2);
    PRINT 'Integration.Onboarding.Password seeded (empty — set the encrypted value from the UI).';
END
ELSE
    PRINT 'Integration.Onboarding.Password already exists - skipped.';
GO

PRINT '2026_07_042 complete - FCCM onboarding credentials moved to Configuration.';
GO
