-- =========================================================================
-- 02_migrate_appsettings_to_config.sql
-- -------------------------------------------------------------------------
-- Controlled migration of UAT appsettings values into the Configuration
-- Management database (dbo.TmX_Configuration_Value), with completeness
-- checks and BEFORE/AFTER validation. Idempotent + re-runnable.
--
-- CONTEXT
--   Migrations 2026_07_036..048 already created the catalogue
--   (TmX_Configuration_Definition) and seeded a base Global value per key.
--   On a fresh UAT DB those base values are the shipped defaults. This
--   script lets you OVERRIDE the environment-specific keys with the real
--   UAT appsettings values, and PROVES the DB matches your intended values.
--
-- WHAT IT DOES
--   1. You declare the UAT values in the #Uat block below (Section 1).
--   2. The script builds a migration PLAN over every *bridgeable* setting
--      (the exact rule DbConfigurationProvider uses:
--         Is_Active=1 AND Is_Sensitive=0 AND Data_Type<>'LogModule'
--         AND Category_Key<>'logging'),
--      so no applicable setting is missed.
--   3. Pre-flight validation (always runs, even in dry-run):
--        - override keys that don't match any Definition  -> abort
--        - secret keys placed in #Uat (must go via UI)    -> abort
--        - unreplaced <<PROVIDE_UAT_VALUE>> sentinels      -> abort
--        - bridgeable keys with NO value anywhere (MISSING)-> abort
--        - BEFORE comparison: appsettings(target) vs current Config value
--   4. Apply (only when @Apply = 1) inside a transaction: upsert the Global
--      value + append a History row, ONLY where the value actually changes.
--   5. AFTER comparison + PASS/FAIL reconciliation.
--
--   Secrets (Is_Sensitive=1: Integration.FccmOracleConnection,
--   Integration.Onboarding.Password, Email.Password) are NEVER written here
--   — they are stored ENCRYPTED and must be set from Configuration → (UI).
--   They are listed at the end so nothing is forgotten.
--
-- RUN — dry run first (validates + previews, commits nothing):
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -W -i "<path>\02_migrate_appsettings_to_config.sql"
-- Then set @Apply = 1 (below) and run again to commit.
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
GO

-- ======================= CONTROL SWITCHES ================================
DECLARE @Apply  bit           = 0;                       -- 0 = DRY RUN (no commit) | 1 = APPLY
DECLARE @Actor  nvarchar(100) = N'Config migration (appsettings->DB, UAT)';
DECLARE @Now    datetime2(3)  = SYSUTCDATETIME();
DECLARE @Sentinel nvarchar(50) = N'<<PROVIDE_UAT_VALUE>>';

-- ======================= 1. UAT VALUES (EDIT ME) =========================
-- One row per setting you want to set from the UAT appsettings.json.
-- * Keys NOT listed here keep whatever value the catalogue seed already
--   holds (that is fine for values that don't change per environment).
-- * Replace every <<PROVIDE_UAT_VALUE>> with the real UAT value, or DELETE
--   the row if the seeded default is already correct for UAT.
-- * You may add ANY bridgeable key below (see the report for the full list).
-- * Do NOT put secrets here (they are rejected) — set them in the UI.
DECLARE @Uat TABLE (Configuration_Key nvarchar(200) PRIMARY KEY, Uat_Value nvarchar(max) NULL);

INSERT INTO @Uat (Configuration_Key, Uat_Value) VALUES
  -- ---- FCCM / Integration (environment endpoints & org identity) ----
  (N'Integration.FccmOnboardingUrl',            @Sentinel),   -- e.g. http://<uat-fccm-host>:7001/InitiateOnboardingService/OB/Initiate
  (N'Integration.CustomerMasterBaseUrl',        @Sentinel),
  (N'Integration.FccmOracleSchema',             @Sentinel),
  (N'Integration.TradeCaseDefaults.OwnerOrg',   @Sentinel),
  (N'Integration.TradeCaseDefaults.OwnerId',    @Sentinel),
  -- ---- Onboarding / External Verification (KYC provider) ----
  (N'Onboarding.BaseUrl',                       @Sentinel),   -- KYC screening provider URL in UAT
  -- ---- Reports / Documents (server paths) ----
  (N'Reports.ChromiumExecutablePath',           @Sentinel),
  -- ---- Email (SMTP) ----
  (N'Email.Enabled',                            @Sentinel),   -- true|false
  (N'Email.Host',                               @Sentinel),
  (N'Email.Port',                               @Sentinel),
  (N'Email.From',                               @Sentinel),
  (N'Email.UseSsl',                             @Sentinel),
  (N'Email.Username',                           @Sentinel);
-- (add / remove rows as needed for your UAT appsettings)

-- ======================= 2. BUILD THE PLAN ===============================
-- Every bridgeable (runtime-consumed) non-secret setting + its current
-- effective value + the requested UAT override.
IF OBJECT_ID('tempdb..#Plan') IS NOT NULL DROP TABLE #Plan;

-- One row per bridgeable non-secret setting, with its current value, the
-- requested override, the resolved target, and the classified action.
-- (Action/Status are computed in the SELECT INTO — no ALTER-then-reference,
--  which would fail compile in the same batch.)
;WITH base AS (
    SELECT
        d.Configuration_Definition_ID           AS Def_ID,
        d.Configuration_Key                      AS [Key],
        d.Category_Key                           AS Category,
        d.Data_Type                              AS Type,
        v.Configuration_Value_ID                 AS Val_ID,
        v.Configuration_Value                    AS DB_Value,
        COALESCE(v.Configuration_Value, d.Default_Value)             AS Current_Effective,
        u.Uat_Value                              AS Uat_Override,
        COALESCE(u.Uat_Value, v.Configuration_Value, d.Default_Value) AS Target_Value,
        CAST(CASE WHEN u.Configuration_Key IS NOT NULL THEN 1 ELSE 0 END AS bit) AS Has_Override
    FROM        dbo.TmX_Configuration_Definition d
    LEFT JOIN   dbo.TmX_Configuration_Value v
           ON   v.Configuration_Definition_ID = d.Configuration_Definition_ID
          AND   v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
    LEFT JOIN   @Uat u ON u.Configuration_Key = d.Configuration_Key
    WHERE       d.Is_Active = 1
      AND       d.Is_Sensitive = 0
      AND       d.Data_Type <> 'LogModule'
      AND       d.Category_Key <> 'logging'   -- bridge rule (mirrors DbConfigurationProvider)
)
SELECT
    Def_ID, [Key], Category, Type, Val_ID, DB_Value, Current_Effective,
    Uat_Override, Target_Value, Has_Override,
    CASE WHEN Target_Value IS NULL THEN 'MISSING' ELSE 'OK' END AS Status,
    CASE
        WHEN Target_Value IS NULL THEN 'BLOCK'
        WHEN Val_ID IS NULL THEN 'INSERT'
        WHEN ISNULL(DB_Value, N'~~null~~') <> ISNULL(Target_Value, N'~~null~~') THEN 'UPDATE'
        ELSE 'NOCHANGE'
    END AS Action
INTO #Plan
FROM base;

-- ======================= 3. PRE-FLIGHT VALIDATION ========================
PRINT '============================================================';
PRINT CONCAT(' Config migration  |  MODE = ', CASE WHEN @Apply = 1 THEN 'APPLY (will commit)' ELSE 'DRY RUN (no commit)' END);
PRINT CONCAT(' DB: ', DB_NAME(), '   UTC: ', CONVERT(varchar(19), @Now, 120));
PRINT '============================================================';

DECLARE @errCount int = 0;

-- 3a. override keys that don't match any bridgeable Definition
PRINT '';
PRINT '--- [pre] override keys not matching a bridgeable Definition (typo / secret / logging / unknown) ---';
SELECT u.Configuration_Key AS Unmatched_Override_Key
FROM   @Uat u
WHERE  NOT EXISTS (SELECT 1 FROM #Plan p WHERE p.[Key] = u.Configuration_Key);
IF @@ROWCOUNT > 0 SET @errCount += 1;

-- 3b. secrets accidentally placed in #Uat
PRINT '';
PRINT '--- [pre] secret keys in #Uat (NOT allowed here — set via UI, encrypted) ---';
SELECT u.Configuration_Key AS Secret_Key_In_Uat
FROM   @Uat u
JOIN   dbo.TmX_Configuration_Definition d ON d.Configuration_Key = u.Configuration_Key
WHERE  d.Is_Sensitive = 1;
IF @@ROWCOUNT > 0 SET @errCount += 1;

-- 3c. unreplaced sentinels
PRINT '';
PRINT '--- [pre] unreplaced <<PROVIDE_UAT_VALUE>> sentinels (replace or delete the row) ---';
SELECT Configuration_Key AS Needs_A_Value FROM @Uat WHERE Uat_Value = @Sentinel;
IF @@ROWCOUNT > 0 SET @errCount += 1;

-- 3d. completeness: bridgeable keys with NO value anywhere
PRINT '';
PRINT '--- [pre] bridgeable settings with NO value anywhere (MISSING — must be provided) ---';
SELECT [Key], Category, Type FROM #Plan WHERE Status = 'MISSING' ORDER BY Category, [Key];
IF @@ROWCOUNT > 0 SET @errCount += 1;

-- 3e. BEFORE comparison — appsettings(target) vs current Config value
PRINT '';
PRINT '--- [BEFORE] target (appsettings) vs current Config value ---';
SELECT
    Category, [Key], Action,
    LEFT(CONVERT(nvarchar(max), Current_Effective), 120) AS Current_Config_Value,
    LEFT(CONVERT(nvarchar(max), Target_Value), 120)      AS Target_Appsettings_Value,
    CASE WHEN ISNULL(Current_Effective,N'~') = ISNULL(Target_Value,N'~')
         THEN 'match' ELSE 'DIFFERS' END                 AS Comparison
FROM   #Plan
ORDER BY CASE Action WHEN 'INSERT' THEN 0 WHEN 'UPDATE' THEN 1 WHEN 'NOCHANGE' THEN 2 ELSE 3 END,
         Category, [Key];

PRINT '';
SELECT
    SUM(CASE WHEN Action = 'INSERT'   THEN 1 ELSE 0 END) AS Will_Insert,
    SUM(CASE WHEN Action = 'UPDATE'   THEN 1 ELSE 0 END) AS Will_Update,
    SUM(CASE WHEN Action = 'NOCHANGE' THEN 1 ELSE 0 END) AS No_Change,
    SUM(CASE WHEN Action = 'BLOCK'    THEN 1 ELSE 0 END) AS Blocking_Missing
FROM #Plan;

IF @errCount > 0
BEGIN
    PRINT '';
    PRINT CONCAT('!! Pre-flight FAILED with ', @errCount, ' issue group(s). Fix the rows listed above. Nothing was changed.');
    RETURN;
END
PRINT '';
PRINT 'Pre-flight OK.';

-- ======================= 4. APPLY (only if @Apply = 1) ===================
IF @Apply = 0
BEGIN
    PRINT '';
    PRINT 'DRY RUN — no changes committed. Set @Apply = 1 and re-run to migrate.';
    RETURN;
END

BEGIN TRY
    BEGIN TRAN;

    -- 4a. INSERT new Global values (definition had no active Global row)
    INSERT INTO dbo.TmX_Configuration_Value
        (Configuration_Definition_ID, Configuration_Value, Is_Active,
         Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
    SELECT p.Def_ID, p.Target_Value, 1, @Actor, @Now, @Actor, @Now
    FROM   #Plan p
    WHERE  p.Action = 'INSERT';
    DECLARE @ins int = @@ROWCOUNT;

    -- history for inserts
    INSERT INTO dbo.TmX_Configuration_History
        (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value,
         Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
    SELECT p.Def_ID, v.Configuration_Value_ID, NULL, p.Target_Value,
           'Create', 'Global', @Actor, @Now
    FROM   #Plan p
    JOIN   dbo.TmX_Configuration_Value v
           ON v.Configuration_Definition_ID = p.Def_ID
          AND v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
    WHERE  p.Action = 'INSERT';

    -- 4b. UPDATE changed Global values (history first, capturing old value)
    INSERT INTO dbo.TmX_Configuration_History
        (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value,
         Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
    SELECT p.Def_ID, p.Val_ID, p.DB_Value, p.Target_Value,
           'Update', 'Global', @Actor, @Now
    FROM   #Plan p
    WHERE  p.Action = 'UPDATE';

    UPDATE v
      SET  v.Configuration_Value = p.Target_Value,
           v.Last_Updated_By     = @Actor,
           v.Last_Updated_Date   = @Now
    FROM   dbo.TmX_Configuration_Value v
    JOIN   #Plan p ON p.Val_ID = v.Configuration_Value_ID
    WHERE  p.Action = 'UPDATE';
    DECLARE @upd int = @@ROWCOUNT;

    PRINT CONCAT('Applied: ', @ins, ' inserted, ', @upd, ' updated.');

    -- ======================= 5. AFTER validation =========================
    PRINT '';
    PRINT '--- [AFTER] effective Config value vs target — should all MATCH ---';
    SELECT
        p.Category, p.[Key],
        LEFT(CONVERT(nvarchar(max),
             COALESCE(v.Configuration_Value, d.Default_Value)), 120) AS Effective_After,
        LEFT(CONVERT(nvarchar(max), p.Target_Value), 120)            AS Target_Value,
        CASE WHEN ISNULL(COALESCE(v.Configuration_Value, d.Default_Value), N'~')
                = ISNULL(p.Target_Value, N'~') THEN 'MATCH' ELSE 'MISMATCH' END AS Result
    FROM   #Plan p
    JOIN   dbo.TmX_Configuration_Definition d ON d.Configuration_Definition_ID = p.Def_ID
    LEFT JOIN dbo.TmX_Configuration_Value v
           ON v.Configuration_Definition_ID = p.Def_ID
          AND v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
    ORDER BY p.Category, p.[Key];

    DECLARE @mismatch int = (
        SELECT COUNT(*)
        FROM   #Plan p
        LEFT JOIN dbo.TmX_Configuration_Value v
               ON v.Configuration_Definition_ID = p.Def_ID
              AND v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
        JOIN   dbo.TmX_Configuration_Definition d ON d.Configuration_Definition_ID = p.Def_ID
        WHERE  ISNULL(COALESCE(v.Configuration_Value, d.Default_Value), N'~') <> ISNULL(p.Target_Value, N'~'));

    IF @mismatch > 0
    BEGIN
        PRINT CONCAT('!! AFTER check found ', @mismatch, ' mismatch(es) — rolling back.');
        ROLLBACK TRAN;
        RETURN;
    END

    COMMIT TRAN;
    PRINT '';
    PRINT 'MIGRATION COMMITTED — all targets reconciled (0 mismatches).';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRAN;
    PRINT CONCAT('!! ERROR — rolled back: ', ERROR_MESSAGE());
    THROW;
END CATCH
GO

-- ======================= 6. SECRETS reminder (never migrated by SQL) =====
PRINT '';
PRINT '--- Secrets (Is_Sensitive = 1) — set these from Configuration UI (stored encrypted) ---';
SELECT
    d.Configuration_Key AS Secret_Key,
    d.Category_Key      AS Category,
    CASE WHEN v.Configuration_Value IS NOT NULL THEN 'SET' ELSE 'NOT SET' END AS Status
FROM        dbo.TmX_Configuration_Definition d
LEFT JOIN   dbo.TmX_Configuration_Value v
       ON   v.Configuration_Definition_ID = d.Configuration_Definition_ID
      AND   v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
WHERE       d.Is_Sensitive = 1
ORDER BY    d.Category_Key, d.Configuration_Key;
GO
