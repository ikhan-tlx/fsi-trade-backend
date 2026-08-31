-- =========================================================================
-- 2026_08_050_AlignPeriodicReviewerAuthorizerRole.sql
-- -------------------------------------------------------------------------
-- FIX: periodic-review KYC halts at "Sent To Authorizer" in environments whose
-- KYC_Workflow scheme names its authorizer actor differently from the role the
-- reviewer service-user holds.
--
-- The reviewer (PERIODIC REVIEWER, GUID F1E2D3C4-…EEFF) is seeded with role
-- 'Authorizer KYC' (2026_07_008 / 2026_07_010). The auto-driver must satisfy the
-- scheme's authorizer CheckRole rule, which matches on role NAME. If the scheme's
-- authorizer actor Value is (e.g.) 'Authorizer' — as in UAT — the reviewer is not
-- in the pool and can never auto-authorize.
--
-- This migration READS the KYC_Workflow scheme's authorizer CheckRole actor value
-- from THIS database and ensures the reviewer is mapped to that role (in addition
-- to any it already holds). No scheme change; safe for human authorizers.
--
-- Idempotent / environment-agnostic:
--   • Demo (scheme actor = 'Authorizer KYC'): reviewer already holds it -> no-op.
--   • UAT  (scheme actor = 'Authorizer'):    adds the 'Authorizer' mapping.
--
-- Run:
--   sqlcmd -S "<server>" -d <db> -E -b -i "<path>\2026_08_050_AlignPeriodicReviewerAuthorizerRole.sql"
-- =========================================================================
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

DECLARE @Reviewer NVARCHAR(50) = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';
DECLARE @Now DATETIME = GETUTCDATE(), @End DATETIME = '9999-12-31';

-- 1. Derive the scheme's authorizer role NAME (the CheckRole actor that is not Admin).
DECLARE @xml XML = (SELECT TRY_CAST(CAST(Scheme AS NVARCHAR(MAX)) AS XML)
                    FROM dbo.WorkflowScheme WHERE Code = N'KYC_Workflow');

DECLARE @AuthRole NVARCHAR(200) = (
    SELECT TOP 1 a.value('@Value','nvarchar(200)')
    FROM @xml.nodes('//*[local-name()="Actor"]') AS t(a)
    WHERE a.value('@Rule','nvarchar(50)')  = N'CheckRole'
      AND a.value('@Value','nvarchar(200)') <> N'Admin'
    ORDER BY (SELECT 1));
print @AuthRole
IF @AuthRole IS NULL OR LTRIM(RTRIM(@AuthRole)) = N''
BEGIN
    PRINT 'Could not read a CheckRole authorizer actor from KYC_Workflow (JSON actor refs, missing scheme, or unexpected shape).';
    PRINT 'Run database\diagnostics\PeriodicReview_Halt_Diagnostic.sql, note the authorizer actor Value, and map the reviewer to that role manually.';
    RETURN;
END
PRINT 'KYC_Workflow authorizer role (from scheme) = ''' + @AuthRole + '''.';

-- 2. Ensure that role exists.
DECLARE @RoleId INT = (SELECT TOP 1 Role_ID FROM dbo.TmX_Role WHERE Role_Name = @AuthRole ORDER BY Role_ID);
IF @RoleId IS NULL
BEGIN
    PRINT 'Role ''' + @AuthRole + ''' does not exist in TmX_Role — cannot map the reviewer. Create the role (or confirm the scheme value) first.';
    RETURN;
END

-- 3. Map the reviewer to it (idempotent).
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_User_Role_Mapping
               WHERE User_Id = @Reviewer AND Role_Id = @RoleId
                 AND (Active_Flag = 1 OR Active_Flag IS NULL))
BEGIN
    INSERT dbo.TmX_User_Role_Mapping
        (Tenant_Id, User_Id, Role_Id, Effective_Start_Date, Effective_End_Date, Active_Flag, Created_By, Created_Date)
    VALUES
        (1, @Reviewer, @RoleId, @Now, @End, 1, N'System (2026_08_050)', @Now);
    PRINT 'Mapped PERIODIC REVIEWER -> role ''' + @AuthRole + ''' (Role_ID ' + CAST(@RoleId AS VARCHAR(10)) + ').';
END
ELSE
    PRINT 'PERIODIC REVIEWER already holds role ''' + @AuthRole + ''' — no change.';
GO

PRINT '2026_08_050 complete. Recycle the app pool if the OptimaJet runtime caches role membership, then re-run a periodic review.';
GO
