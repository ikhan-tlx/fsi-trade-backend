-- =========================================================================
-- 01_config_current_env_report.sql
-- -------------------------------------------------------------------------
-- Configuration Management — CURRENT ENVIRONMENT validation report.
--
-- Read-only. Lists every configuration setting in the current environment
-- (Definition catalogue) with its currently-effective value and where that
-- value comes from, mirroring the runtime resolution used by
-- DbConfigurationProvider:
--        EffectiveValue = COALESCE(active Global Value, Definition Default)
-- and the SAME bridge predicate the app uses to publish a key into
-- IConfiguration:
--        Is_Active = 1 AND Is_Sensitive = 0 AND Data_Type <> 'LogModule'
--        AND Category_Key <> 'logging' AND EffectiveValue IS NOT NULL
--
-- Use it to (a) validate the current config, and (b) as the BEFORE / AFTER
-- snapshot around the migration in 02_migrate_appsettings_to_config.sql.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -W -i "<path>\01_config_current_env_report.sql"
-- (add  -o "<path>\config_report.txt"  to capture, or run in SSMS.)
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
GO

PRINT '============================================================';
PRINT ' Configuration Management — current environment report';
PRINT CONCAT(' Server: ', @@SERVERNAME, '   DB: ', DB_NAME(), '   UTC: ', CONVERT(varchar(19), SYSUTCDATETIME(), 120));
PRINT '============================================================';
GO

-- ---------- 1. Full catalogue with effective values ----------
PRINT '';
PRINT '--- [1] All settings (current env) with effective values ---';

SELECT
    c.Display_Order                                   AS Cat_Order,
    d.Category_Key                                    AS Category,
    d.Display_Order                                   AS Ord,
    d.Configuration_Key                               AS [Key],
    d.Data_Type                                       AS Type,
    CAST(d.Is_Sensitive  AS int)                      AS Secret,
    CAST(d.Is_Editable   AS int)                      AS Editable,
    CAST(d.Applies_On_Restart AS int)                 AS Restart,
    LEFT(CONVERT(nvarchar(max), d.Default_Value), 200)          AS Default_Value,
    LEFT(CONVERT(nvarchar(max), v.Configuration_Value), 200)    AS DB_Value,
    -- Effective value = what the app actually reads (Value overrides Default).
    LEFT(CONVERT(nvarchar(max),
         COALESCE(v.Configuration_Value, d.Default_Value)), 200) AS Effective_Value,
    CASE
        WHEN d.Is_Sensitive = 1
             THEN 'Secret (encrypted; not shown here)'
        WHEN v.Configuration_Value IS NOT NULL
             THEN 'DB value (explicitly set)'
        WHEN d.Default_Value IS NOT NULL
             THEN 'Default (no DB value set)'
        ELSE 'UNSET / blank'
    END                                               AS Value_Source,
    CASE WHEN v.Configuration_Value IS NOT NULL THEN 1 ELSE 0 END AS Is_Set,
    -- Does this key get published into IConfiguration (IOptions bridge)?
    CASE WHEN d.Is_Active = 1 AND d.Is_Sensitive = 0
              AND d.Data_Type <> 'LogModule' AND d.Category_Key <> 'logging'
              AND COALESCE(v.Configuration_Value, d.Default_Value) IS NOT NULL
         THEN 'Yes' ELSE 'No' END                     AS Bridged,
    CASE WHEN v.Pending_Value IS NOT NULL THEN 'PENDING APPROVAL' ELSE '' END AS Pending,
    v.Last_Updated_By                                 AS Updated_By,
    CONVERT(varchar(19), v.Last_Updated_Date, 120)    AS Updated_Date,
    (SELECT COUNT(*) FROM dbo.TmX_Configuration_History h
       WHERE h.Configuration_Definition_ID = d.Configuration_Definition_ID) AS Hist_Rows
FROM        dbo.TmX_Configuration_Definition d
LEFT JOIN   dbo.TmX_Configuration_Value v
       ON   v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND   v.Is_Active = 1
      AND   v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
LEFT JOIN   dbo.TmX_Configuration_Category c
       ON   c.Category_Key = d.Category_Key
ORDER BY    c.Display_Order, d.Category_Key, d.Display_Order, d.Configuration_Key;

-- ---------- 2. Summary by category ----------
PRINT '';
PRINT '--- [2] Summary by category ---';

SELECT
    c.Display_Order                                                       AS Cat_Order,
    d.Category_Key                                                        AS Category,
    COUNT(*)                                                              AS Settings,
    SUM(CASE WHEN v.Configuration_Value IS NOT NULL THEN 1 ELSE 0 END)    AS With_DB_Value,
    SUM(CASE WHEN v.Configuration_Value IS NULL
             AND d.Default_Value IS NOT NULL THEN 1 ELSE 0 END)           AS Using_Default,
    SUM(CASE WHEN d.Is_Sensitive = 1 THEN 1 ELSE 0 END)                   AS Secrets,
    SUM(CASE WHEN COALESCE(v.Configuration_Value, d.Default_Value) IS NULL
             AND d.Is_Sensitive = 0 THEN 1 ELSE 0 END)                    AS Blank_NonSecret
FROM        dbo.TmX_Configuration_Definition d
LEFT JOIN   dbo.TmX_Configuration_Value v
       ON   v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND   v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
LEFT JOIN   dbo.TmX_Configuration_Category c ON c.Category_Key = d.Category_Key
GROUP BY    c.Display_Order, d.Category_Key
ORDER BY    c.Display_Order, d.Category_Key;

-- ---------- 3. Secrets status (value never shown) ----------
PRINT '';
PRINT '--- [3] Secrets (Is_Sensitive = 1) — presence only, set via UI (encrypted) ---';

SELECT
    d.Configuration_Key                               AS [Key],
    d.Category_Key                                    AS Category,
    CASE WHEN v.Configuration_Value IS NOT NULL
         THEN 'SET (encrypted value present)'
         ELSE 'NOT SET' END                           AS Secret_Status,
    v.Last_Updated_By                                 AS Updated_By,
    CONVERT(varchar(19), v.Last_Updated_Date, 120)    AS Updated_Date
FROM        dbo.TmX_Configuration_Definition d
LEFT JOIN   dbo.TmX_Configuration_Value v
       ON   v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND   v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE       d.Is_Sensitive = 1
ORDER BY    d.Category_Key, d.Configuration_Key;

-- ---------- 4. Completeness — non-secret keys with a BLANK effective value ----------
--            (these would need a value explicitly provided before go-live)
PRINT '';
PRINT '--- [4] Non-secret settings with NO effective value (blank) — must be provided ---';

SELECT
    d.Configuration_Key   AS [Key],
    d.Category_Key        AS Category,
    d.Data_Type           AS Type
FROM        dbo.TmX_Configuration_Definition d
LEFT JOIN   dbo.TmX_Configuration_Value v
       ON   v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND   v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE       d.Is_Active = 1
  AND       d.Is_Sensitive = 0
  AND       COALESCE(v.Configuration_Value, d.Default_Value) IS NULL
ORDER BY    d.Category_Key, d.Configuration_Key;

PRINT '';
PRINT 'Report complete.';
GO
