-- =========================================================================
-- 2026_07_040_BrandingConfiguration.sql
-- -------------------------------------------------------------------------
-- Branding / Appearance settings for the Configuration Management module
-- (/admin/configuration). Adds a 'branding' category + 4 definitions
-- (brand color, app title, app subtitle, default theme) and seeds their
-- Global values + a Create history row each.
--
-- *** VALUES ARE DECLARED, AND MANDATORY (Section 2b) ***
--   Every value is declared once in @VAL and edited per environment. The
--   migration ABORTS if any setting is not declared. Metadata (@DEF, incl. the
--   help Description) is fixed.
--
-- These keys drive the application shell AND the pre-auth login page, read via
-- the public GET /api/v1/Appearance endpoint. Editable, non-sensitive, apply
-- immediately. GLOBAL only. Idempotent.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_07_040_BrandingConfiguration.sql"
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

-- 2a. CATALOGUE METADATA (fixed).
DECLARE @DEF TABLE (
    K nvarchar(200), Name nvarchar(200), Descr nvarchar(1000), Cat nvarchar(50), Dt nvarchar(20),
    Def nvarchar(max), Unit nvarchar(40), Rules nvarchar(max),
    Editable bit, Sensitive bit, Approval bit, Restart bit, Ord int);

INSERT INTO @DEF (K,Name,Descr,Cat,Dt,Def,Unit,Rules,Editable,Sensitive,Approval,Restart,Ord) VALUES
('Branding.BrandColor', N'Brand color',        N'Primary brand / logo color used across the application shell and the login page.', N'branding', N'String', N'#c7000b',                NULL, N'{"format":"color"}',                                            1, 0, 0, 0, 10),
('Branding.AppTitle',   N'Application title',   N'Application title shown in the browser tab, the header and on the login page.',     N'branding', N'String', N'ICBC Trade Compliance', NULL, NULL,                                                             1, 0, 0, 0, 20),
('Branding.AppSubtitle',N'Application subtitle',N'Application subtitle shown beneath the title on the login page.',                    N'branding', N'String', N'工商银行 · KYC Platform', NULL, NULL,                                                            1, 0, 0, 0, 30),
('Branding.DefaultTheme',N'Default theme',      N'Default UI theme applied for new sessions before a user preference is set.',        N'branding', N'Enum',   N'light',                 NULL, N'{"allowedValues":["light","dark","corporate","high-contrast"]}', 1, 0, 0, 0, 40);

-- 2b. ENVIRONMENT VALUES *** REQUIRED — declare every value ***
DECLARE @VAL TABLE (K nvarchar(200) PRIMARY KEY, Val nvarchar(max) NULL);
INSERT INTO @VAL (K, Val) VALUES
('Branding.BrandColor',  N'#c7000b'),                 -- << ENV >>  tenant brand color
('Branding.AppTitle',    N'ICBC Trade Compliance'),
('Branding.AppSubtitle', N'工商银行 · KYC Platform'),
('Branding.DefaultTheme',N'light');

-- 2c. COMPLETENESS GUARD (presence-based; empty allowed but surfaced)
IF EXISTS (SELECT 1 FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K))
BEGIN
    PRINT '!! Declared value(s) reference an UNKNOWN setting key:';
    SELECT v.K AS Unknown_Value_Key FROM @VAL v WHERE NOT EXISTS (SELECT 1 FROM @DEF d WHERE d.K = v.K);
    RAISERROR('2026_07_040 aborted: unknown key in the value declaration. Nothing seeded.', 16, 1);
    RETURN;
END;
IF EXISTS (SELECT 1 FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K))
BEGIN
    PRINT '!! These settings are NOT DECLARED — every value MUST be declared:';
    SELECT d.Cat AS Category, d.K AS Undeclared_Setting_Key
    FROM @DEF d WHERE d.Sensitive = 0 AND NOT EXISTS (SELECT 1 FROM @VAL v WHERE v.K = d.K)
    ORDER BY d.Cat, d.K;
    RAISERROR('2026_07_040 aborted: a setting is not declared. Nothing seeded.', 16, 1);
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
PRINT CONCAT('Section 2 - branding definitions inserted: ', @@ROWCOUNT);

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
PRINT CONCAT('Section 2 - branding Global values inserted: ', @@ROWCOUNT);

INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @DEF d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
     AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Section 2 - branding Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_07_040 complete - Branding / Appearance category + declared values seeded.';
GO
