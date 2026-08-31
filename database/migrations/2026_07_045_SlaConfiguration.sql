-- =========================================================================
-- 2026_07_045_SlaConfiguration.sql
-- -------------------------------------------------------------------------
-- SLA / Deadlines settings for the Configuration Management module. Adds a
-- 'sla' category + 3 definitions (enabled, default window hours, at-risk
-- percent) and seeds their Global values + a Create history row each.
--
-- *** VALUES ARE DECLARED, AND MANDATORY (Section 2b) ***  Every value is
--   declared once in @VAL; the migration ABORTS if any is not declared.
--
-- Drives SlaBreachScanService via IOptionsMonitor<SlaOptions> (hot-reload).
-- Editable, non-sensitive, apply immediately. GLOBAL only. Idempotent.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_045_SlaConfiguration.sql"
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

-- 2a. CATALOGUE METADATA (fixed).
DECLARE @DEF TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @DEF (K,Name,Descr,Cat,Dt,Def,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Sla.Enabled',       N'SLA scanner enabled', N'Master on/off for the background SLA-breach notification scanner.',                              N'sla', N'Boolean', N'true', NULL,      NULL,                          1, 0, 0, 0, 10),
('Sla.DefaultHours',  N'SLA window (hours)',  N'Deadline window in hours from when a work item enters its current state before it is breached.', N'sla', N'Integer', N'24',   N'hours',   N'{"min":1,"max":720,"step":1}', 1, 0, 0, 0, 20),
('Sla.AtRiskPercent', N'At-risk threshold %', N'Raise the at-risk notification once elapsed time reaches this percent of the SLA window.',       N'sla', N'Integer', N'80',   N'percent', N'{"min":10,"max":99,"step":5}', 1, 0, 0, 0, 30);

-- 2b. ENVIRONMENT VALUES *** REQUIRED — declare every value ***
DECLARE @VAL TABLE (K nvarchar(200) PRIMARY KEY, Val nvarchar(max) NULL);
INSERT INTO @VAL (K, Val) VALUES
('Sla.Enabled',       N'true'),
('Sla.DefaultHours',  N'24'),
('Sla.AtRiskPercent', N'80');

-- 2c. COMPLETENESS GUARD (presence-based)
IF EXISTS (SELECT 1 FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K))
BEGIN
    PRINT '!! Declared value(s) reference an UNKNOWN setting key:';
    SELECT v.K AS Unknown_Value_Key FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K);
    RAISERROR('2026_07_045 aborted: unknown key in the value declaration. Nothing seeded.', 16, 1);
    RETURN;
END;
IF EXISTS (SELECT 1 FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K))
BEGIN
    PRINT '!! These settings are NOT DECLARED — every value MUST be declared:';
    SELECT d.Cat AS Category, d.K AS Undeclared_Setting_Key
    FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K)
    ORDER BY d.Cat, d.K;
    RAISERROR('2026_07_045 aborted: a setting is not declared. Nothing seeded.', 16, 1);
    RETURN;
END;
PRINT 'Section 2 - value declaration complete: every setting is declared.';

-- insert definitions (metadata)
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, d.Descr, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @DEF d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - sla definitions inserted: ', @@ROWCOUNT);

-- insert the Global value from @VAL
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, v.Val, 1, @who, @now, @who, @now
FROM   @VAL v
JOIN   @DEF d   ON d.K = v.K
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = v.K
WHERE  d.Sensitive = 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value cv
                   WHERE cv.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND cv.Branch_ID IS NULL AND cv.Role_ID IS NULL AND cv.Environment IS NULL);
PRINT CONCAT('Section 2 - sla Global values inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @DEF d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - sla Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_07_045 complete - SLA / Deadlines category + declared values seeded.';
GO
