-- =========================================================================
-- 2026_08_056_SlaUnify.sql
-- -------------------------------------------------------------------------
-- Merge the two SLA concepts into ONE days-based window.
--   Before: TradeRepository.SlaDays (days) drove ONLY the grid aging badge;
--           Sla.DefaultHours (hours) drove ONLY the breach/at-risk scanner.
--   After:  a single Sla.WindowDays (days) drives BOTH — the grid badge
--           (GetTransactionStatsQueryHandler now reads SlaOptions.WindowDays)
--           and the SlaBreachScanService (now TimeSpan.FromDays(WindowDays)).
--
--   * Add Sla.WindowDays (Integer, days) in the 'sla' category.
--   * Deactivate Sla.DefaultHours (replaced) and TradeRepository.SlaDays (grid
--     now shares the SLA window). The Trade Repository category becomes empty,
--     so it is removed too.
--
-- Requires the matching code change (SlaOptions.WindowDays + the two consumers)
-- to be deployed. Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_056_SlaUnify.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @who nvarchar(100) = N'System (2026_08_056)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- 1) Add the unified Sla.WindowDays definition (if absent)
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Configuration_Key = N'Sla.WindowDays')
BEGIN
    INSERT INTO dbo.TmX_Configuration_Definition
        (Configuration_Key, Display_Name, Description, Help_Detail, Category_Key, Data_Type, Default_Value,
         Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
         Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    VALUES
        (N'Sla.WindowDays', N'SLA window', N'The single SLA deadline, in days.',
         N'The one SLA window (days) from when a transaction enters its current state. Drives BOTH the SLA-breach/at-risk notification scanner (SlaBreachScanService) AND the Trade Repository grid aging badge (GetTransactionStatsQueryHandler) — one setting, no divergence.',
         N'sla', N'Integer', N'5', N'{"min":1,"max":90,"step":1}', N'days',
         1, 0, 0, 0, 1, 15, @who, @now, @who, @now);
    PRINT 'Sla.WindowDays definition inserted.';
END
ELSE
    PRINT 'Sla.WindowDays already present.';

-- Global value for Sla.WindowDays (if absent)
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.Configuration_Definition_ID, N'5', 1, @who, @now, @who, @now
FROM   dbo.TmX_Configuration_Definition d
WHERE  d.Configuration_Key = N'Sla.WindowDays'
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = d.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Sla.WindowDays Global value inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT d.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   dbo.TmX_Configuration_Definition d
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = d.Configuration_Definition_ID
WHERE  d.Configuration_Key = N'Sla.WindowDays'
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = d.Configuration_Definition_ID);
PRINT CONCAT('Sla.WindowDays history rows: ', @@ROWCOUNT);

-- 2) Deactivate the two superseded settings
UPDATE dbo.TmX_Configuration_Definition
   SET Is_Active = 0, Last_Updated_By = @who, Last_Updated_Date = @now
 WHERE Configuration_Key IN (N'Sla.DefaultHours', N'TradeRepository.SlaDays') AND Is_Active = 1;
PRINT CONCAT('Superseded SLA settings deactivated: ', @@ROWCOUNT);

-- 3) Remove the now-empty Trade Repository category
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition WHERE Category_Key = N'repo' AND Is_Active = 1)
BEGIN
    DELETE FROM dbo.TmX_Configuration_Category WHERE Category_Key = N'repo';
    PRINT CONCAT('Empty Trade Repository (repo) category removed: ', @@ROWCOUNT);
END
ELSE
    PRINT 'repo category still has active settings - left in place.';
GO

PRINT '2026_08_056 complete - SLA unified onto Sla.WindowDays (days).';
GO
