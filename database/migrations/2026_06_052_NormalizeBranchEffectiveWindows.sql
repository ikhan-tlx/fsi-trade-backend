-- =========================================================================
-- 2026_06_052_NormalizeBranchEffectiveWindows.sql
-- -------------------------------------------------------------------------
-- Symptom: GET /CompanyBranch/lov returns NO branches for a user (the
-- transaction-create branch dropdown is empty) in an environment where the
-- data was imported/seeded with a closed effective window or Active_Flag = 0.
--
-- ListBranchLovQueryHandler requires, for "now":
--   TmX_Company_Branch_Users_Mapping: Active_Flag=1 AND Start<=now AND End>=now
--   TmX_Company_Branch              : Active_Flag=1 AND Start<=now AND End>=now
-- Any row failing one of these is filtered out. There is no FE control to fix
-- the mapping's effective dates / active flag, so this backfill does it.
--
-- It only touches rows that are CURRENTLY excluded (inactive, not-yet-started,
-- or expired) and makes them live: Active_Flag=1, Start<=now, End=9999-12-31.
-- Idempotent. REVIEW the optional scoping filters before running in prod.
-- =========================================================================
SET NOCOUNT ON;

DECLARE @now datetime      = GETUTCDATE();
DECLARE @far datetime      = '9999-12-31';
DECLARE @who nvarchar(100) = N'System (2026_06_052)';

-- Optional scoping: leave NULL to apply to all rows in all tenants.
DECLARE @TenantId int      = NULL;          -- e.g. 1
DECLARE @UserId   nvarchar(50) = NULL;      -- e.g. a specific analyst's Id

-------------------------------------------------------------------------
-- 0. BEFORE — how many rows are currently excluded by the LOV filter
-------------------------------------------------------------------------
SELECT 'Branches excluded (before)' AS Metric, COUNT(*) AS Cnt
FROM   dbo.TmX_Company_Branch
WHERE  (Active_Flag = 0 OR Effective_Start_Date > @now OR Effective_End_Date < @now)
  AND  (@TenantId IS NULL OR Tenant_Id = @TenantId);

SELECT 'User-branch mappings excluded (before)' AS Metric, COUNT(*) AS Cnt
FROM   dbo.TmX_Company_Branch_Users_Mapping
WHERE  (Active_Flag = 0 OR Effective_Start_Date > @now OR Effective_End_Date < @now)
  AND  (@TenantId IS NULL OR Tenant_ID = @TenantId)
  AND  (@UserId   IS NULL OR User_Id   = @UserId);

-------------------------------------------------------------------------
-- 1. Branch rows (TmX_Company_Branch)
-------------------------------------------------------------------------
UPDATE dbo.TmX_Company_Branch
SET    Active_Flag          = 1,
       Effective_Start_Date = CASE WHEN Effective_Start_Date > @now THEN @now ELSE Effective_Start_Date END,
       Effective_End_Date   = CASE WHEN Effective_End_Date   < @now THEN @far  ELSE Effective_End_Date   END,
       Last_Updated_By      = @who,
       Last_Updated_Date    = @now
WHERE  (Active_Flag = 0 OR Effective_Start_Date > @now OR Effective_End_Date < @now)
  AND  (@TenantId IS NULL OR Tenant_Id = @TenantId);
PRINT CONCAT('TmX_Company_Branch normalised: ', @@ROWCOUNT, ' row(s).');

-------------------------------------------------------------------------
-- 2. User-branch mapping rows (TmX_Company_Branch_Users_Mapping)
-------------------------------------------------------------------------
UPDATE dbo.TmX_Company_Branch_Users_Mapping
SET    Active_Flag          = 1,
       Effective_Start_Date = CASE WHEN Effective_Start_Date > @now THEN @now ELSE Effective_Start_Date END,
       Effective_End_Date   = CASE WHEN Effective_End_Date   < @now THEN @far  ELSE Effective_End_Date   END,
       Last_Updated_By      = @who,
       Last_Updated_Date    = @now
WHERE  (Active_Flag = 0 OR Effective_Start_Date > @now OR Effective_End_Date < @now)
  AND  (@TenantId IS NULL OR Tenant_ID = @TenantId)
  AND  (@UserId   IS NULL OR User_Id   = @UserId);
PRINT CONCAT('TmX_Company_Branch_Users_Mapping normalised: ', @@ROWCOUNT, ' row(s).');

-------------------------------------------------------------------------
-- 3. AFTER — should be 0 excluded
-------------------------------------------------------------------------
SELECT 'Branches excluded (after)' AS Metric, COUNT(*) AS Cnt
FROM   dbo.TmX_Company_Branch
WHERE  (Active_Flag = 0 OR Effective_Start_Date > @now OR Effective_End_Date < @now)
  AND  (@TenantId IS NULL OR Tenant_Id = @TenantId);

SELECT 'User-branch mappings excluded (after)' AS Metric, COUNT(*) AS Cnt
FROM   dbo.TmX_Company_Branch_Users_Mapping
WHERE  (Active_Flag = 0 OR Effective_Start_Date > @now OR Effective_End_Date < @now)
  AND  (@TenantId IS NULL OR Tenant_ID = @TenantId)
  AND  (@UserId   IS NULL OR User_Id   = @UserId);

PRINT '2026_06_052 complete.';
