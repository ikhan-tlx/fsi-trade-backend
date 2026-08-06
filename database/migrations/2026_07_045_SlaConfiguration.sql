-- =========================================================================
-- 2026_07_045_SlaConfiguration.sql
-- -------------------------------------------------------------------------
-- SLA / Deadlines settings for the Configuration Management module
-- (/admin/configuration). Adds a new 'sla' category plus 3 definitions
-- (enabled, default window hours, at-risk percent) and seeds their Global
-- values + a Create history row each.
--
-- These keys tune the SLA-breach notification scanner (SlaBreachScanService):
-- the scanner reads Sla.Enabled / Sla.DefaultHours / Sla.AtRiskPercent via the
-- DB->IConfiguration bridge (IOptionsMonitor<SlaOptions>, hot-reload), so an
-- operator can turn the scanner on/off and re-tune the window from this screen
-- with no restart. Editable, non-sensitive and apply immediately
-- (Requires_Approval = 0), so the existing PUT /api/v1/Configuration write path
-- (Configuration.Manage) manages them with no maker/checker step.
--
-- GLOBAL only: the TmX_Configuration_* tables have no tenant column; every
-- value row is seeded with Branch_ID / Role_ID / Environment = NULL.
--
-- Idempotent: category via MERGE (WHEN NOT MATCHED); definitions / values /
-- history guarded by NOT EXISTS on their natural keys. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_045_SlaConfiguration.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Seed category ----------
MERGE dbo.TmX_Configuration_Category AS tgt
USING (VALUES
    ('sla', N'SLA / Deadlines', N'Service-level deadlines and the SLA-breach notification scanner', 16, NULL, 0, 0)
) AS src (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
ON tgt.Category_Key = src.Category_Key
WHEN NOT MATCHED THEN
    INSERT (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
    VALUES (src.Category_Key, src.Title, src.Description, src.Display_Order, src.Verify_Kind, src.Is_Flagged, src.Is_Logging);
PRINT CONCAT('Section 1 - sla category upserted: ', @@ROWCOUNT);
GO

-- ---------- 2. Seed definitions + Global values ----------
DECLARE @who nvarchar(100) = N'System (2026_07_045)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- staging: one row per SLA setting (Value = the current Global value = Default_Value)
DECLARE @D TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Val nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @D (K,Name,Descr,Cat,Dt,Def,Val,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Sla.Enabled',       N'SLA scanner enabled', N'Master on/off for the background SLA-breach notification scanner.',                              N'sla', N'Boolean', N'true', N'true', NULL,        NULL,                          1, 0, 0, 0, 10),
('Sla.DefaultHours',  N'SLA window (hours)',  N'Deadline window in hours from when a work item enters its current state before it is breached.', N'sla', N'Integer', N'24',   N'24',   N'hours',     N'{"min":1,"max":720,"step":1}', 1, 0, 0, 0, 20),
('Sla.AtRiskPercent', N'At-risk threshold %', N'Raise the at-risk notification once elapsed time reaches this percent of the SLA window.',       N'sla', N'Integer', N'80',   N'80',   N'percent',   N'{"min":10,"max":99,"step":5}', 1, 0, 0, 0, 30);

-- insert new definitions (skip any Configuration_Key that already exists)
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, d.Descr, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @D d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - sla definitions inserted: ', @@ROWCOUNT);

-- insert the Global active value for each
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Val, 1, @who, @now, @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Section 2 - sla Global values inserted: ', @@ROWCOUNT);

-- seed a Create history row per new sla definition (audit trail baseline)
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - sla Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_07_045 complete - SLA / Deadlines category + definitions + Global values seeded.';
GO
