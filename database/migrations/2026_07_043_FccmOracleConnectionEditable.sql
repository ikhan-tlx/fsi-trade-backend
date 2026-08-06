-- =========================================================================
-- 2026_07_043_FccmOracleConnectionEditable.sql
-- -------------------------------------------------------------------------
-- Make the FCCM Oracle connection a REAL Configuration setting instead of an
-- inert row. Previously Integration.FccmOracleConnection existed as a Secret
-- definition (Is_Editable = 0, empty DB value) while the value was actually
-- read from appsettings — so the config row did nothing.
--
-- Now the value is stored ENCRYPTED in the module and read decrypted at use
-- (FccmOracleReader + the Test-connection probe source it via ISecretConfigReader,
-- with an appsettings fallback). This migration marks the definition EDITABLE so
-- an admin can set/rotate it from the UI (encrypted on write). The value itself
-- is set from the UI (or the API), not seeded here.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_043_FccmOracleConnectionEditable.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_07_043)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

UPDATE dbo.TmX_Configuration_Definition
   SET Is_Editable = 1,
       Description  = N'FCCM Oracle database connection string. Stored ENCRYPTED at rest; set/rotate it from Configuration → FCCM/Integration (Update value). Read decrypted at point of use; never displayed or logged.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = 'Integration.FccmOracleConnection' AND Is_Editable = 0;

IF @@ROWCOUNT > 0
    PRINT 'Integration.FccmOracleConnection is now editable (encrypted secret, managed in Configuration).';
ELSE
    PRINT 'Integration.FccmOracleConnection already editable or not found - no change.';
GO

PRINT '2026_07_043 complete.';
GO
