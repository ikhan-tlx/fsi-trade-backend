/* ============================================================================
   2026_07_002_AutoFlagAggregateEngine.sql

   Phase 1 of the aggregate auto-flag engine. All additive and idempotent —
   no existing flag, column, or row changes behaviour.

   1. TmX_Flag_Rule.Right_Factor        (decimal(18,4), NULL)
      TmX_Flag_Rule.Right_Factor_Unit   (nvarchar(12),  NULL: 'Multiplier'|'Percent')
        Optional scale applied to the RIGHT operand of a condition, so a rule can
        read "LEFT > (KYC field x 1.5)" or "> (KYC field x 60%)". At evaluation:
          rhs = fieldValue * (Unit = 'Percent' ? Factor/100 : Factor)
        NULL Factor => x1 (unchanged). Existing rules are unaffected.

   2. TmX_AutoFlag_Job
        Work queue for ASYNC (aggregate / cross-transaction) auto-flag evaluation.
        A row is enqueued at transaction save when an aggregate flag applies to the
        transaction's product/sub-product; a background worker claims it, computes
        the aggregate at runtime (compute-on-read, no fact table), evaluates the
        aggregate flags and stages TmX_Transaction_Flag. Non-aggregate flags keep
        being evaluated synchronously at save and never enqueue.

   3. TmX_Product_Aggregate_Field_Map
        Small config: for a product, which UDF field code plays a given role
        ('Amount', 'Counterparty', ...). Lets value aggregates (Sum/Avg/...) read
        the right UDF field per product without hardcoding field names. Seeded per
        product during Phase-1 rollout (separate seed once field codes are
        confirmed against live data).

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
-- Filtered indexes below require these SET options ON (sqlcmd defaults QUOTED_IDENTIFIER OFF).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* 1. Right_Factor + Right_Factor_Unit on TmX_Flag_Rule ----------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_Flag_Rule')
                 AND name = 'Right_Factor')
BEGIN
    ALTER TABLE dbo.TmX_Flag_Rule ADD [Right_Factor] decimal(18,4) NULL;
    PRINT 'Added dbo.TmX_Flag_Rule.Right_Factor.';
END
ELSE
    PRINT 'dbo.TmX_Flag_Rule.Right_Factor already exists — skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.TmX_Flag_Rule')
                 AND name = 'Right_Factor_Unit')
BEGIN
    ALTER TABLE dbo.TmX_Flag_Rule ADD [Right_Factor_Unit] nvarchar(12) NULL;
    PRINT 'Added dbo.TmX_Flag_Rule.Right_Factor_Unit.';
END
ELSE
    PRINT 'dbo.TmX_Flag_Rule.Right_Factor_Unit already exists — skipped.';
GO

/* 2. TmX_AutoFlag_Job — async evaluation queue ------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.objects
               WHERE object_id = OBJECT_ID('dbo.TmX_AutoFlag_Job') AND type = 'U')
BEGIN
    CREATE TABLE dbo.TmX_AutoFlag_Job
    (
        [Job_ID]         int IDENTITY(1,1) NOT NULL
            CONSTRAINT [PK_TmX_AutoFlag_Job] PRIMARY KEY,
        [Transaction_ID] int            NOT NULL,
        [Customer_Code]  nvarchar(64)   NULL,
        [Trigger_Type]   nvarchar(20)   NOT NULL
            CONSTRAINT [DF_TmX_AutoFlag_Job_Trigger] DEFAULT ('Save'),
        [Status]         nvarchar(20)   NOT NULL
            CONSTRAINT [DF_TmX_AutoFlag_Job_Status] DEFAULT ('Pending'),
        [Attempts]       int            NOT NULL
            CONSTRAINT [DF_TmX_AutoFlag_Job_Attempts] DEFAULT (0),
        [Error]          nvarchar(max)  NULL,
        [Enqueued_Date]  datetime2(3)   NOT NULL
            CONSTRAINT [DF_TmX_AutoFlag_Job_Enqueued] DEFAULT (SYSUTCDATETIME()),
        [Claimed_Date]   datetime2(3)   NULL,
        [Processed_Date] datetime2(3)   NULL
    );
    PRINT 'Created dbo.TmX_AutoFlag_Job.';
END
ELSE
    PRINT 'dbo.TmX_AutoFlag_Job already exists — skipped.';
GO

/* Coalesce: at most one PENDING job per transaction (filtered unique). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_TmX_AutoFlag_Job_PendingTxn'
                 AND object_id = OBJECT_ID('dbo.TmX_AutoFlag_Job'))
BEGIN
    CREATE UNIQUE INDEX [UQ_TmX_AutoFlag_Job_PendingTxn]
        ON dbo.TmX_AutoFlag_Job ([Transaction_ID])
        WHERE [Status] = 'Pending';
    PRINT 'Created filtered unique index UQ_TmX_AutoFlag_Job_PendingTxn.';
END
ELSE
    PRINT 'UQ_TmX_AutoFlag_Job_PendingTxn already exists — skipped.';
GO

/* Worker claim ordering. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_TmX_AutoFlag_Job_StatusEnqueued'
                 AND object_id = OBJECT_ID('dbo.TmX_AutoFlag_Job'))
BEGIN
    CREATE INDEX [IX_TmX_AutoFlag_Job_StatusEnqueued]
        ON dbo.TmX_AutoFlag_Job ([Status], [Enqueued_Date]);
    PRINT 'Created index IX_TmX_AutoFlag_Job_StatusEnqueued.';
END
ELSE
    PRINT 'IX_TmX_AutoFlag_Job_StatusEnqueued already exists — skipped.';
GO

/* 3. TmX_Product_Aggregate_Field_Map — per-product UDF field roles ----------- */
IF NOT EXISTS (SELECT 1 FROM sys.objects
               WHERE object_id = OBJECT_ID('dbo.TmX_Product_Aggregate_Field_Map') AND type = 'U')
BEGIN
    CREATE TABLE dbo.TmX_Product_Aggregate_Field_Map
    (
        [Map_ID]      int IDENTITY(1,1) NOT NULL
            CONSTRAINT [PK_TmX_Product_Aggregate_Field_Map] PRIMARY KEY,
        [Product_ID]  int          NOT NULL,
        [Role]        nvarchar(40) NOT NULL,     -- 'Amount' | 'Counterparty' | ...
        [Field_Code]  nvarchar(100) NOT NULL,
        [Active_Flag] bit          NOT NULL
            CONSTRAINT [DF_TmX_ProdAggMap_Active] DEFAULT (1),
        [Created_By]  nvarchar(100) NULL,
        [Created_Date] datetime2(3) NOT NULL
            CONSTRAINT [DF_TmX_ProdAggMap_Created] DEFAULT (SYSUTCDATETIME())
    );
    PRINT 'Created dbo.TmX_Product_Aggregate_Field_Map.';
END
ELSE
    PRINT 'dbo.TmX_Product_Aggregate_Field_Map already exists — skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_TmX_ProdAggMap_ProductRoleField'
                 AND object_id = OBJECT_ID('dbo.TmX_Product_Aggregate_Field_Map'))
BEGIN
    CREATE UNIQUE INDEX [UQ_TmX_ProdAggMap_ProductRoleField]
        ON dbo.TmX_Product_Aggregate_Field_Map ([Product_ID], [Role], [Field_Code]);
    PRINT 'Created unique index UQ_TmX_ProdAggMap_ProductRoleField.';
END
ELSE
    PRINT 'UQ_TmX_ProdAggMap_ProductRoleField already exists — skipped.';
GO

PRINT '2026_07_002_AutoFlagAggregateEngine.sql complete.';
GO
