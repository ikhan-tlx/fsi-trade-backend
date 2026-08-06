-- =========================================================================
-- 2026_07_040_BrandingConfiguration.sql
-- -------------------------------------------------------------------------
-- Branding / Appearance settings for the Configuration Management module
-- (/admin/configuration). Adds a new 'branding' category plus 4 definitions
-- (brand color, app title, app subtitle, default theme) and seeds their
-- Global values + a Create history row each.
--
-- These keys drive the application shell AND the pre-auth login page (title /
-- logo / theme), read via the public GET /api/v1/Appearance endpoint. They are
-- editable, non-sensitive and apply immediately (Requires_Approval = 0), so the
-- existing PUT /api/v1/Configuration write path (Configuration.Manage) manages
-- them with no maker/checker step and no new write endpoint.
--
-- GLOBAL only: the TmX_Configuration_* tables have no tenant column; every
-- value row is seeded with Branch_ID / Role_ID / Environment = NULL.
--
-- Idempotent: category via MERGE (WHEN NOT MATCHED); definitions / values /
-- history guarded by NOT EXISTS on their natural keys. Safe to re-run.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_040_BrandingConfiguration.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Seed category ----------
MERGE dbo.TmX_Configuration_Category AS tgt
USING (VALUES
    ('branding', N'Branding / Appearance', N'Application title, logo color and default theme', 15, NULL, 0, 0)
) AS src (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
ON tgt.Category_Key = src.Category_Key
WHEN NOT MATCHED THEN
    INSERT (Category_Key, Title, Description, Display_Order, Verify_Kind, Is_Flagged, Is_Logging)
    VALUES (src.Category_Key, src.Title, src.Description, src.Display_Order, src.Verify_Kind, src.Is_Flagged, src.Is_Logging);
PRINT CONCAT('Section 1 - branding category upserted: ', @@ROWCOUNT);
GO

-- ---------- 2. Seed definitions + Global values ----------
DECLARE @who nvarchar(100) = N'System (2026_07_040)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- staging: one row per branding setting (Value = the current Global value = Default_Value)
DECLARE @D TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Val nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @D (K,Name,Descr,Cat,Dt,Def,Val,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Branding.BrandColor', N'Brand color',        N'Primary brand / logo color used across the application shell and the login page.', N'branding', N'String', N'#c7000b',                N'#c7000b',                NULL, N'{"format":"color"}',                                            1, 0, 0, 0, 10),
('Branding.AppTitle',   N'Application title',   N'Application title shown in the browser tab, the header and on the login page.',     N'branding', N'String', N'ICBC Trade Compliance', N'ICBC Trade Compliance', NULL, NULL,                                                             1, 0, 0, 0, 20),
('Branding.AppSubtitle',N'Application subtitle',N'Application subtitle shown beneath the title on the login page.',                    N'branding', N'String', N'工商银行 · KYC Platform', N'工商银行 · KYC Platform', NULL, NULL,                                                             1, 0, 0, 0, 30),
('Branding.DefaultTheme',N'Default theme',      N'Default UI theme applied for new sessions before a user preference is set.',        N'branding', N'Enum',   N'light',                 N'light',                 NULL, N'{"allowedValues":["light","dark","corporate","high-contrast"]}', 1, 0, 0, 0, 40);

-- insert new definitions (skip any Configuration_Key that already exists)
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, d.Descr, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       d.Editable, d.Sensitive, d.Approval, d.Restart, 1, d.Ord, @who, @now, @who, @now
FROM   @D d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Section 2 - branding definitions inserted: ', @@ROWCOUNT);

-- insert the Global active value for each
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Val, 1, @who, @now, @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value v
                   WHERE v.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL);
PRINT CONCAT('Section 2 - branding Global values inserted: ', @@ROWCOUNT);

-- seed a Create history row per new branding definition (audit trail baseline)
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @D d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - branding Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_07_040 complete - Branding / Appearance category + definitions + Global values seeded.';
GO
