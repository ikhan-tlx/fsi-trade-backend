-- =========================================================================
-- 2026_07_047_EmailSmtpOnly.sql
-- -------------------------------------------------------------------------
-- SMTP is now the ONLY email transport. The delivery-mode switch is removed,
-- so this migration deletes the two config keys that 2026_07_046 seeded:
--   Email.DeliveryMode      (Smtp | PickupDirectory)
--   Email.PickupDirectory   (.eml drop folder)
-- Definitions + their Global values + history rows are removed. The remaining
-- Email.* keys (Enabled, Host, Port, Username, UseSsl, From, MinSeverity) and
-- the appsettings Email:Password secret are unchanged.
--
-- Idempotent: DELETEs are naturally no-ops if the keys are already gone.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_047_EmailSmtpOnly.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @keys TABLE (K nvarchar(200) PRIMARY KEY);
INSERT INTO @keys (K) VALUES (N'Email.DeliveryMode'), (N'Email.PickupDirectory');

-- 1. history rows for those definitions
DELETE h
FROM dbo.TmX_Configuration_History h
JOIN dbo.TmX_Configuration_Definition d
      ON d.Configuration_Definition_ID = h.Configuration_Definition_ID
JOIN @keys k ON k.K = d.Configuration_Key;
PRINT CONCAT('Removed Email delivery-mode history rows: ', @@ROWCOUNT);

-- 2. scoped values for those definitions
DELETE v
FROM dbo.TmX_Configuration_Value v
JOIN dbo.TmX_Configuration_Definition d
      ON d.Configuration_Definition_ID = v.Configuration_Definition_ID
JOIN @keys k ON k.K = d.Configuration_Key;
PRINT CONCAT('Removed Email delivery-mode value rows: ', @@ROWCOUNT);

-- 3. the definitions themselves
DELETE d
FROM dbo.TmX_Configuration_Definition d
JOIN @keys k ON k.K = d.Configuration_Key;
PRINT CONCAT('Removed Email delivery-mode definitions: ', @@ROWCOUNT);
GO

PRINT '2026_07_047 complete - Email delivery-mode config removed (SMTP-only).';
GO
