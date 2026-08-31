-- =========================================================================
-- PeriodicReview_Halt_Diagnostic.sql   (READ-ONLY — no data is modified)
-- -------------------------------------------------------------------------
-- Purpose: confirm WHY the periodic-review auto-flow halts at "Sent To
-- Authorizer" instead of finalising to CMS. The auto-driver acts as one
-- identity ("PERIODIC REVIEWER") that must satisfy BOTH the maker rule
-- (IsCreator, on SendToAuthorizer) AND the checker rule (CheckRole, on the
-- AssignToMe / AuthorizeOrProceedToFccm authorize commands).
--
-- CheckRole (see FsiWorkflowRuleProvider) passes only when the caller
--   (a) has a WorkflowInbox row for the process, AND
--   (b) holds a role whose NAME equals the scheme's authorizer actor value.
-- The inbox is populated by WorkflowInboxSynchronizer from exactly that same
-- role name. So if the reviewer's role name != the scheme's authorizer actor
-- value, the reviewer is never queued and can never authorize -> the txn is
-- parked in the human authorizer pool.
--
-- Run (targets whatever DB you pass with -d, so it works on demo AND UAT):
--   sqlcmd -S "<server>" -d <db> -E -W -i "<path>\PeriodicReview_Halt_Diagnostic.sql"
-- =========================================================================
-- NB: no hardcoded USE — runs against the -d database. XML methods in section 2
-- require QUOTED_IDENTIFIER ON.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT '================ 1. Reviewer identity + roles ================';
-- The configured reviewer id lives in appsettings PeriodicReview:ReviewerUserId
-- (GUID F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF). Show the user and every role it holds.
SELECT u.User_ID, u.User_Name, u.Active_Flag, u.Status
FROM dbo.TmX_User u
WHERE u.User_Name = N'PERIODIC REVIEWER'
   OR u.User_ID   = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF';

SELECT u.User_Name, r.Role_Name AS ReviewerRole
FROM dbo.TmX_User u
JOIN dbo.TmX_User_Role_Mapping m ON m.User_Id = u.User_ID
JOIN dbo.TmX_Role r              ON r.Role_ID = m.Role_ID
WHERE (u.User_Name = N'PERIODIC REVIEWER'
    OR u.User_ID   = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF')
  AND (m.Active_Flag = 1 OR m.Active_Flag IS NULL);
GO

PRINT '================ 2. KYC scheme authorizer actor value(s) ================';
-- Presence checks (bulletproof, no XML parsing): which role-name spelling is
-- actually written into the KYC_Workflow scheme?
SELECT
    CASE WHEN Scheme LIKE N'%Authorizer KYC%' THEN 1 ELSE 0 END AS Scheme_Has_AuthorizerKYC,
    CASE WHEN Scheme LIKE N'%Analyst KYC%'    THEN 1 ELSE 0 END AS Scheme_Has_AnalystKYC,
    CASE WHEN Scheme LIKE N'%AssignToMe%'                THEN 1 ELSE 0 END AS Scheme_Has_AssignToMe,
    CASE WHEN Scheme LIKE N'%AuthorizeOrProceedToFccm%'  THEN 1 ELSE 0 END AS Scheme_Has_AuthorizeCmd,
    CASE WHEN Scheme LIKE N'%CheckRole%'                 THEN 1 ELSE 0 END AS Scheme_Has_CheckRole
FROM dbo.WorkflowScheme
WHERE Code = N'KYC_Workflow';

-- Exact actor list via XML (works when the scheme is valid XML with no default
-- namespace on <Actor>). Shows the authorizer actor's Value = the role name the
-- reviewer MUST hold. If this returns no rows, rely on the presence checks above
-- and on section 4 (the resolved pool from a live stuck process).
;WITH s AS (
    SELECT TRY_CAST(CAST(Scheme AS NVARCHAR(MAX)) AS XML) AS x
    FROM dbo.WorkflowScheme WHERE Code = N'KYC_Workflow'
)
SELECT
    a.value('@Name',  'nvarchar(200)')  AS ActorName,
    a.value('@Rule',  'nvarchar(200)')  AS [Rule],
    a.value('@Value', 'nvarchar(max)')  AS [Value]
FROM s CROSS APPLY s.x.nodes('//*[local-name()="Actor"]') AS t(a)
WHERE s.x IS NOT NULL;
GO

PRINT '================ 3. KYC txns currently parked at Sent To Authorizer, created by the reviewer ================';
SELECT TOP 50
       t.Transaction_Number,
       t.Process_Instance_Id,
       pi.StateName,
       pi.ActivityName,
       t.Created_By,
       t.Created_Date
FROM dbo.TmX_Transaction t
JOIN dbo.WorkflowProcessInstance pi ON pi.Id = t.Process_Instance_Id
WHERE pi.StateName LIKE N'%Authoriz%'
  AND (t.Created_By = N'PERIODIC REVIEWER'
    OR t.User_Id    = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF')
ORDER BY t.Created_Date DESC;
GO

PRINT '================ 4. Who is in the authorizer POOL for those stuck processes (and is the reviewer among them?) ================';
-- For each stuck reviewer-created process, list the WorkflowInbox members and the
-- roles they hold. Expectation for the bug: the pool = the real Authorizers
-- (role "Authorizer"), and the reviewer (role "Authorizer KYC") is ABSENT.
;WITH stuck AS (
    SELECT t.Process_Instance_Id AS Pid, t.Transaction_Number AS Txn
    FROM dbo.TmX_Transaction t
    JOIN dbo.WorkflowProcessInstance pi ON pi.Id = t.Process_Instance_Id
    WHERE pi.StateName LIKE N'%Authoriz%'
      AND (t.Created_By = N'PERIODIC REVIEWER'
        OR t.User_Id    = N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF')
)
SELECT s.Txn,
       s.Pid,
       inbox.IdentityId,
       u.User_Name          AS PoolMember,
       r.Role_Name          AS PoolMemberRole,
       CASE WHEN inbox.IdentityId = CAST(N'F1E2D3C4-B5A6-47C8-9901-AABBCCDDEEFF' AS uniqueidentifier)
            THEN 1 ELSE 0 END AS IsReviewer
FROM stuck s
JOIN dbo.WorkflowInbox inbox ON inbox.ProcessId = s.Pid
LEFT JOIN dbo.TmX_User u              ON u.User_ID = CAST(inbox.IdentityId AS nvarchar(50))
LEFT JOIN dbo.TmX_User_Role_Mapping m ON m.User_Id = u.User_ID AND (m.Active_Flag = 1 OR m.Active_Flag IS NULL)
LEFT JOIN dbo.TmX_Role r              ON r.Role_ID = m.Role_ID
ORDER BY s.Txn, PoolMember;
GO

PRINT '================ VERDICT ================';
PRINT 'If section 1 shows the reviewer role = "Authorizer KYC" but section 2/4 show';
PRINT 'the scheme authorizer actor / pool role = "Authorizer" (no reviewer row with';
PRINT 'IsReviewer=1 in section 4), the halt is a ROLE-NAME MISMATCH: align the';
PRINT 'reviewer''s TmX_User_Role_Mapping to the scheme''s authorizer actor value.';
GO
