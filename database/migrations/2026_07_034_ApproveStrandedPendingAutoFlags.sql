-- =========================================================================
-- 2026_07_034_ApproveStrandedPendingAutoFlags.sql
-- -------------------------------------------------------------------------
-- One-off UAT remediation (data fix, NOT a schema change). Idempotent.
--
-- CONTEXT
--   Before the approval flow was unified, an auto flag was approved inline by
--   flipping TmX_Flag_Catalogue directly (Status='Active', Active_Flag=1). After
--   deploying the new maker/checker flow, flags that were left mid-approval sit
--   at Status='PendingApproval' with NO TmX_Request row, so they can't be
--   approved from the new Requests queue.
--
--   This script APPROVES those stuck flags directly — same effect the old
--   ApproveAutoFlagCommand had — without creating anything in TmX_Request.
--   For each flag at Status='PendingApproval' it sets:
--       Status      = 'Active'
--       Active_Flag = 1                (flag starts evaluating on transactions)
--       Approved_By / Approved_Date    (stamped)
--       Last_Updated_By / Last_Updated_Date
--
-- SCOPE
--   Status = 'PendingApproval' AND Is_Deleted = 0.
--   (Manual red flags use the replay model and never sit at 'PendingApproval'
--    on the catalogue row, so this effectively targets auto flags only. If your
--    data differs, add a flag-type filter — see the commented clause below.)
--
-- SAFETY
--   * PREVIEW first (read-only), then the UPDATE.
--   * Idempotent: after the run the rows are 'Active', so re-running matches
--     nothing.
--   * Set @ApprovedBy to whoever should be recorded as the approver.
--   * Does NOT touch TmX_Request. The optional final block only CLOSES any
--     already-existing pending 0005 request for these flags (so the queue isn't
--     left showing a contradictory "pending" item) — it never inserts a row.
--     Legacy flags have no such row, so that block is usually a no-op.
--
--   IMPORTANT: change the database name on the USE line below to your UAT
--   database, and confirm the column names match your instance.
--
-- Run (point -d at your UAT DB):
--   sqlcmd -S "<UAT-SERVER>" -d <UAT_DB> -E -i "<path>\2026_07_034_ApproveStrandedPendingAutoFlags.sql"
-- =========================================================================

USE [ICBC_DEMO];   -- <<< CHANGE to your UAT database name
GO
SET NOCOUNT ON;
GO

DECLARE @ApprovedBy NVARCHAR(200) = N'UAT deployment (bulk approval)';   -- <<< set the approver to record
DECLARE @Now        DATETIME2      = SYSUTCDATETIME();

-- ── PREVIEW: which flags will be approved (read-only) ────────────────────
PRINT '--- Auto flags currently stuck at PendingApproval (will be approved) ---';
SELECT  Flag_ID, Flag_Code, Status, Active_Flag, Submitted_By, Submitted_Date
FROM    dbo.TmX_Flag_Catalogue
WHERE   Status = N'PendingApproval'
  AND   Is_Deleted = 0
  -- AND Flag_Type_Lkp_ID IN (SELECT Lookup_ID FROM dbo.TmX_Lookup
  --        WHERE Lookup_Type = N'FLAG_TYPE' AND Hidden_Value IN (N'Automated', N'Auto'))
ORDER BY Flag_ID;

-- ── APPROVE: activate the stuck flags directly ──────────────────────────
UPDATE  dbo.TmX_Flag_Catalogue
SET     Status            = N'Active',
        Active_Flag       = 1,
        Approved_By       = @ApprovedBy,
        Approved_Date     = @Now,
        Last_Updated_By   = @ApprovedBy,
        Last_Updated_Date = @Now
WHERE   Status = N'PendingApproval'
  AND   Is_Deleted = 0;
  -- AND Flag_Type_Lkp_ID IN (SELECT Lookup_ID FROM dbo.TmX_Lookup
  --        WHERE Lookup_Type = N'FLAG_TYPE' AND Hidden_Value IN (N'Automated', N'Auto'));

PRINT CONCAT('Approved (activated) ', @@ROWCOUNT, ' auto flag(s).');
GO

-- ── OPTIONAL cleanup: close any already-existing pending 0005 request for a
--    now-approved flag, so the Requests queue doesn't show a stale "pending"
--    item. Inserts nothing; only updates rows that already exist. Harmless
--    (no-op) if none exist — which is the case for purely-legacy flags.
UPDATE  r
SET     r.Request_Status_Code = N'0012',   -- Approved
        r.Last_Updated_By     = N'UAT deployment (bulk approval)',
        r.Last_Updated_Date   = SYSUTCDATETIME()
FROM    dbo.TmX_Request r
JOIN    dbo.TmX_Flag_Catalogue fc
        ON  fc.Flag_ID = TRY_CAST(r.Entity_Id AS INT)
WHERE   r.Request_Type_Code   = N'0005'
  AND   r.Request_Status_Code = N'0011'
  AND   fc.Status             = N'Active'
  AND   fc.Is_Deleted         = 0;

PRINT CONCAT('Closed ', @@ROWCOUNT, ' stale pending 0005 request(s), if any.');
GO

PRINT '2026_07_034 complete — stranded PendingApproval auto flags are now Active.';
GO
