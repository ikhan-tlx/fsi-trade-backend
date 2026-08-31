-- =========================================================================
-- 2026_08_054_ConfigCleanup.sql
-- -------------------------------------------------------------------------
-- Configuration Management cleanup (review feedback):
--   * Branding.BrandGold  -> display name "Secondary colour" (drop "gold").
--   * Email.DeliveryMode + Email.PickupDirectory -> DEACTIVATED. The sender
--     (SmtpEmailSender) is SMTP-only; those two keys have NO code consumer
--     (EmailOptions has no DeliveryMode/PickupDirectory) — see 2026_07_047.
--   * Empty 'notify' category -> removed (no settings reference it; the email
--     settings live under the 'email' category).
--   * TradeRepository.SlaDays -> clarified help so it is not confused with the
--     SLA / Deadlines scanner (Sla.*). Both are live but DIFFERENT: SlaDays is a
--     calendar-day aging threshold for the trade-repository grid badge
--     (GetTransactionStatsQueryHandler); Sla.* is an hours-based deadline for
--     the SlaBreachScanService notifications. Neither is removed.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_054_ConfigCleanup.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_08_054)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- 1) Rename the secondary colour
UPDATE dbo.TmX_Configuration_Definition
   SET Display_Name = N'Secondary colour',
       Description   = N'Secondary brand colour for accents.',
       Help_Detail   = N'Secondary brand accent used alongside Branding.BrandColor in the shell and login page.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'Branding.BrandGold';
PRINT CONCAT('Branding.BrandGold renamed: ', @@ROWCOUNT);

-- 2) Deactivate the dead Email delivery-mode / pickup-directory settings (SMTP-only)
UPDATE dbo.TmX_Configuration_Definition
   SET Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key IN (N'Email.DeliveryMode', N'Email.PickupDirectory')
   AND Is_Active = 1;
PRINT CONCAT('Email delivery-mode/pickup deactivated: ', @@ROWCOUNT);

-- 3) Clarify TradeRepository.SlaDays vs the SLA / Deadlines scanner
UPDATE dbo.TmX_Configuration_Definition
   SET Description = N'Aging threshold (days) for the Trade Repository grid badge.',
       Help_Detail = N'Calendar-day aging threshold (from Transaction_Date) used by GetTransactionStatsQueryHandler to colour the trade-repository grid "aging" badge. SEPARATE from the SLA / Deadlines scanner (Sla.*), which is an hours-based deadline that raises breach notifications on inbox items — different concept, not a duplicate.',
       Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key = N'TradeRepository.SlaDays';
PRINT CONCAT('TradeRepository.SlaDays help clarified: ', @@ROWCOUNT);

-- 4) Remove the empty 'notify' category (only if nothing references it)
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Category_Key = N'notify')
BEGIN
    DELETE FROM dbo.TmX_Configuration_Category WHERE Category_Key = N'notify';
    PRINT CONCAT('Empty ''notify'' category removed: ', @@ROWCOUNT);
END
ELSE
    PRINT '''notify'' category still has definitions - left in place.';
GO

PRINT '2026_08_054 complete - config cleanup (rename, dead email fields, notify category, SLA clarity).';
GO
