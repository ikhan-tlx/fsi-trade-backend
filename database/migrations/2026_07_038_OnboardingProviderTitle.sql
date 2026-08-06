-- =========================================================================
-- 2026_07_038_OnboardingProviderTitle.sql
-- -------------------------------------------------------------------------
-- Third-Party KYC integration (Phase 2, item 1): a configurable provider
-- TITLE so the UI/backend can display any provider name instead of the
-- hardcoded "BRAINS". Sourced from Configuration Management (Onboarding group)
-- and read via the DB->IConfiguration bridge. Default 'BRAINS' preserves the
-- current wording until an admin renames it.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_038_OnboardingProviderTitle.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_07_038)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Onboarding.ProviderTitle')
BEGIN
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        ('Onboarding.ProviderTitle', N'Third-party provider title',
         N'Display name for the external KYC verification provider. Shown on screens, labels, messages and logs (replaces the hardcoded "BRAINS").',
         'onboarding', 'String', N'BRAINS', N'{"required":true}', NULL, 1, 0, 0, 0, 1, 5, @who, @now, @who, @now);

    DECLARE @defId int = (SELECT Configuration_Definition_ID FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Onboarding.ProviderTitle');

    INSERT INTO dbo.TmX_Configuration_Value
        (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES (@defId, N'BRAINS', 1, @who, @now, @who, @now);

    INSERT INTO dbo.TmX_Configuration_History
        (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
    SELECT @defId, (SELECT TOP 1 Configuration_Value_ID FROM dbo.TmX_Configuration_Value WHERE Configuration_Definition_ID = @defId),
           NULL, N'BRAINS', 'Create', 'Global', @who, @now;

    PRINT 'Onboarding.ProviderTitle seeded.';
END
ELSE
    PRINT 'Onboarding.ProviderTitle already exists - skipped.';
GO

PRINT '2026_07_038 complete - Onboarding.ProviderTitle.';
GO
