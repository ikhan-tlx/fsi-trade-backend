-- =========================================================================
-- 2026_07_048_EmailPasswordSecret.sql
-- -------------------------------------------------------------------------
-- Move the SMTP password out of appsettings (Email:Password) into the
-- Configuration Management module as an ENCRYPTED secret:
--
--   Email.Password — Secret, Is_Sensitive=1 → NOT bridged into IConfiguration;
--                    stored ENCRYPTED at rest (ASP.NET Data Protection) and read
--                    DECRYPTED at point of use by SmtpEmailSender via
--                    ISecretConfigReader. Seeded EMPTY here; the encrypted value
--                    is set from Configuration → Email / SMTP (Update value),
--                    which encrypts on write. Masked (••••••) in the catalogue
--                    and history; revealable via the Reveal endpoint.
--
-- Mirrors 2026_07_042 (Integration.Onboarding.Password). Idempotent.
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_048_EmailPasswordSecret.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = 'Email.Password')
BEGIN
    DECLARE @who nvarchar(100) = N'System (2026_07_048)';
    DECLARE @now datetime2(3)  = SYSUTCDATETIME();
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        ('Email.Password', N'SMTP password',
         N'Password for the SMTP account (used with Username). Stored ENCRYPTED at rest; set it from Configuration → Email / SMTP (Update value). Never displayed or logged.',
         'email', 'Secret', N'', NULL, NULL, 1, 1, 0, 0, 1, 90, @who, @now, @who, @now);
    PRINT 'Email.Password seeded (empty — set the encrypted value from the UI).';
END
ELSE
    PRINT 'Email.Password already exists - skipped.';
GO

PRINT '2026_07_048 complete - SMTP password moved to Configuration (encrypted secret).';
GO
