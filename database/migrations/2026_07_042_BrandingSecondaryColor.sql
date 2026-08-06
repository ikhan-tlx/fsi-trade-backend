-- =========================================================================
-- 2026_07_042_BrandingSecondaryColor.sql
-- -------------------------------------------------------------------------
-- Adds ONE more Branding / Appearance setting to the Configuration Management
-- module (/admin/configuration): the secondary / gold accent colour used for
-- the selected menu item and active table columns. Follows 040's pattern:
-- inserts the single 'Branding.BrandGold' definition, seeds its Global value
-- + a Create history row. The 'branding' category already exists (from 040),
-- so it is NOT re-seeded here.
--
-- (Renumbered 041 -> 042: 2026_07_041 was already taken by
--  RetireLegacyOnboardingFields; this branding migration moved to 042.)
--
-- This key drives the application shell AND the pre-auth login page, read via
-- the public GET /api/v1/Configuration/Branding endpoint (returned as
-- brandGold). It is editable, non-sensitive and applies immediately
-- (Requires_Approval = 0), so the existing PUT /api/v1/Configuration write
-- path (Configuration.Manage) manages it with no maker/checker step and no
-- new write endpoint.
--
-- GLOBAL only: the TmX_Configuration_* tables have no tenant column; the value
-- row is seeded with Branch_ID / Role_ID / Environment = NULL.
--
-- Idempotent: definition / value / history guarded by NOT EXISTS on their
-- natural keys. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_042_BrandingSecondaryColor.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Seed definition + Global value ----------
DECLARE @who nvarchar(100) = N'System (2026_07_042)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- staging: the single new branding setting (Value = the current Global value = Default_Value)
DECLARE @D TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Val nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @D (K,Name,Descr,Cat,Dt,Def,Val,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Branding.BrandGold', N'Secondary (gold) colour', N'Secondary/gold accent used for the selected menu item and active table columns.', N'branding', N'String', N'#e8c27a', N'#e8c27a', NULL, N'{"format":"color"}', 1, 0, 0, 0, 15);

-- insert new definition (skip if the Configuration_Key already exists)
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, d.Descr, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @D d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 1 - branding secondary-colour definition inserted: ', @@ROWCOUNT);

-- insert the Global active value
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Val, 1, @who, @now, @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Section 1 - branding secondary-colour Global value inserted: ', @@ROWCOUNT);

-- seed a Create history row for the new definition (audit trail baseline)
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 1 - branding secondary-colour Create history row: ', @@ROWCOUNT);
GO

PRINT '2026_07_042 complete - Branding.BrandGold definition + Global value seeded.';
GO
