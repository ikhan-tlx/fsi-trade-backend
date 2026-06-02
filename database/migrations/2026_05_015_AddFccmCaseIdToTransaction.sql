-- =========================================================================
-- 2026_05_015_AddFccmCaseIdToTransaction.sql
--
-- Slice 9 Phase 1 — scaffolding for the trade-to-FCCM submission pipeline.
--
-- Adds `Fccm_Case_Id` to `dbo.TmX_Transaction`. Populated by
-- `IFccmTradeCaseService.SubmitAsync` after a successful FCCM case
-- insert; serves two purposes:
--
--   1. Idempotency anchor — if Fccm_Case_Id is non-NULL on a transaction,
--      a re-fire of the workflow's terminal command is a no-op (we
--      don't double-submit). Set back to NULL only via the explicit
--      resubmit admin endpoint.
--
--   2. Backlink — FE / reports can display the FCCM case reference
--      against the transaction without a join into the integration
--      staging tables.
--
-- Idempotent — IF NOT EXISTS guard on the column add. Safe to re-run.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.TmX_Transaction')
      AND name      = N'Fccm_Case_Id'
)
BEGIN
    PRINT 'Adding column dbo.TmX_Transaction.Fccm_Case_Id (nvarchar(100), NULL)';

    ALTER TABLE [dbo].[TmX_Transaction]
        ADD [Fccm_Case_Id] [nvarchar](100) NULL;
END
ELSE
BEGIN
    PRINT 'Column dbo.TmX_Transaction.Fccm_Case_Id already exists — skipping ADD.';
END
GO

-- Filtered index — most rows will never have a case id (only finalised
-- transactions do), so the index targets the populated subset for
-- "show me transactions awaiting FCCM result" or "look up by case id"
-- queries.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name      = N'IX_TmX_Transaction_FccmCaseId'
      AND object_id = OBJECT_ID(N'dbo.TmX_Transaction')
)
BEGIN
    PRINT 'Creating filtered index IX_TmX_Transaction_FccmCaseId';

    CREATE INDEX [IX_TmX_Transaction_FccmCaseId]
        ON [dbo].[TmX_Transaction] ([Fccm_Case_Id])
        WHERE [Fccm_Case_Id] IS NOT NULL;
END
ELSE
BEGIN
    PRINT 'Index IX_TmX_Transaction_FccmCaseId already exists — skipping CREATE.';
END
GO

PRINT 'Slice 9 Phase 1 — Fccm_Case_Id column + filtered index ready.';
