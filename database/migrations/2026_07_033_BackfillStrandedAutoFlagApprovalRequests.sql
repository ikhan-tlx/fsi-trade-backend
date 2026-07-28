-- =========================================================================
-- 2026_07_033_BackfillStrandedAutoFlagApprovalRequests.sql
-- -------------------------------------------------------------------------
-- SUPERSEDED — DO NOT RUN.
--
-- This approach (backfilling a pending TmX_Request row per stranded flag so it
-- could be approved through the Requests queue) was rejected: for the UAT
-- deployment we do NOT want to reconstruct approval history in TmX_Request.
--
-- Use instead:
--   2026_07_034_ApproveStrandedPendingAutoFlags.sql
-- which directly approves (activates) the flags stuck at PendingApproval, with
-- no TmX_Request involvement.
-- =========================================================================
PRINT '2026_07_033 is superseded — run 2026_07_034_ApproveStrandedPendingAutoFlags.sql instead.';
GO
