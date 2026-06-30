-- =========================================================================
-- 2026_06_055_RemoveLegacyKycWorkflowCodeAction.sql
--
-- 2026-06-23 — fix the UAT KYC workflow HTTP 500 (OptimaJet CodeActions
-- compile failure on .NET 8).
--
-- PROBLEM
--   The KYC_Workflow scheme carries one inline <CodeAction> copied verbatim
--   from the legacy .NET-Framework app — "SetBlacklistedTransaction" — whose
--   <Usings> import System.Data.Linq (LINQ-to-SQL), TMX.* and LinqToSqlShared
--   and which instantiates LoanApplicationService. OptimaJet compiles every
--   scheme's inline CodeActions with Roslyn at load time. On .NET 8 those
--   namespaces/types do not exist -> CS0234 / CS0246 -> every KYC workflow
--   call (POST /Transaction, GET /Workflow/Process/{id}/Commands) returns 500.
--
-- WHY REMOVING IT IS SAFE (behaviour-preserving)
--   The action is DEAD CODE: "SetBlacklistedTransaction" is only DEFINED; it
--   is never referenced by any activity, transition or actor restriction
--   (verified by diff). The blacklist step is driven by the CheckBlacklist
--   COMMAND (kept intact), which the backend maps to APPLICATION_STATUS 26887
--   ("CheckBlacklist") via Command_Application_Status_Mapping in
--   ExecuteWorkflowCommandHandler. Removing the <CodeActions> block leaves the
--   scheme's Actors, Commands (7), Activities (4) and Transitions (6) exactly
--   as authored — NO workflow-behaviour change, and (unlike transplanting
--   another environment's scheme) it preserves THIS env's own actor/role
--   names (e.g. "Analyst"/"Authorizer", not "Analyst KYC").
--
-- MECHANISM (no schema change)
--   In place, excise the single "<CodeActions> ... </CodeActions>" element
--   from the KYC_Workflow MASTER (dbo.WorkflowScheme) and from every in-flight
--   KYC process SNAPSHOT (dbo.WorkflowProcessScheme) that still carries it.
--   NOTE: OptimaJet stores Scheme as the legacy NTEXT type, which STUFF /
--   CHARINDEX / LEN reject — so the column is CAST to NVARCHAR(MAX) first
--   (via CROSS APPLY) before the block is excised. Live process STATE lives in
--   dbo.WorkflowProcessInstance (untouched), so in-flight KYC cases keep their
--   current stage and simply stop recompiling the dead code.
--
-- SCOPE
--   KYC_Workflow only. Guarded to rows whose CodeActions block contains the
--   legacy LINQ-to-SQL token, so it can never strip a legitimate CodeActions
--   block. (KYC_Workflow has exactly one CodeAction — the dead one.)
--
-- IDEMPOTENT
--   Each UPDATE is guarded by the legacy token + both <CodeActions> tags.
--   After it runs no matching rows remain, so re-running is a no-op.
--
-- POST-DEPLOY
--   Recycle the app pool so the OptimaJet runtime re-reads/re-compiles the
--   cleaned scheme, then: create a KYC transaction -> expect success; open an
--   existing KYC case -> Commands return 200. Import/Export are unaffected.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

-- 1) MASTER scheme ---------------------------------------------------------
UPDATE w
   SET Scheme = STUFF(a.s, p.op, p.cl + LEN(N'</CodeActions>') - p.op, N'')
FROM dbo.WorkflowScheme AS w
CROSS APPLY (SELECT CAST(w.Scheme AS NVARCHAR(MAX)) AS s) AS a
CROSS APPLY (SELECT CHARINDEX(N'<CodeActions>', a.s)  AS op,
                    CHARINDEX(N'</CodeActions>', a.s) AS cl) AS p
WHERE w.Code = N'KYC_Workflow'
  AND p.op > 0 AND p.cl > p.op
  AND a.s LIKE N'%System.Data.Linq%';
PRINT 'KYC_Workflow master cleaned. Rows affected = ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- 2) In-flight KYC process snapshots --------------------------------------
UPDATE ps
   SET Scheme = STUFF(a.s, p.op, p.cl + LEN(N'</CodeActions>') - p.op, N'')
FROM dbo.WorkflowProcessScheme AS ps
CROSS APPLY (SELECT CAST(ps.Scheme AS NVARCHAR(MAX)) AS s) AS a
CROSS APPLY (SELECT CHARINDEX(N'<CodeActions>', a.s)  AS op,
                    CHARINDEX(N'</CodeActions>', a.s) AS cl) AS p
WHERE ps.SchemeCode = N'KYC_Workflow'
  AND p.op > 0 AND p.cl > p.op
  AND a.s LIKE N'%System.Data.Linq%';
PRINT 'KYC_Workflow snapshots cleaned. Rows affected = ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- 3) Verify in-transaction: zero legacy tokens must remain; commit or roll back.
DECLARE @m INT = (SELECT COUNT(*) FROM dbo.WorkflowScheme        WHERE Code=N'KYC_Workflow'       AND Scheme LIKE N'%System.Data.Linq%');
DECLARE @s INT = (SELECT COUNT(*) FROM dbo.WorkflowProcessScheme WHERE SchemeCode=N'KYC_Workflow' AND Scheme LIKE N'%System.Data.Linq%');
DECLARE @cmd INT = (SELECT COUNT(*) FROM dbo.WorkflowScheme      WHERE Code=N'KYC_Workflow'       AND Scheme LIKE N'%CheckBlacklist%'); -- sanity: command preserved
PRINT 'Post-clean: master legacy=' + CAST(@m AS VARCHAR(10))
    + '  snapshots legacy=' + CAST(@s AS VARCHAR(10))
    + '  CheckBlacklist-command-present=' + CAST(@cmd AS VARCHAR(10));

IF (@m + @s) > 0
BEGIN
    RAISERROR('Legacy LINQ tokens still present after clean. Rolling back.', 16, 1);
    ROLLBACK TRAN;
END
ELSE
BEGIN
    COMMIT TRAN;
    PRINT 'Committed. KYC_Workflow has no inline CodeActions; recycle the app pool, then re-test.';
END
GO
