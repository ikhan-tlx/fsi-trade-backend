/* ============================================================================
   2026_07_001_CreateCstmBrains
   ----------------------------------------------------------------------------
   Ensures the legacy dbo.Cstm_Brains comparison table exists. This table is used
   by the ported caseInsertionController risk pipeline (FccmCaseIdPoller Phase B)
   to record, per transaction, the FCCM (TBML) band vs the BRAINS band and the
   reconciled "higher-risk wins" outcome plus its attribution suffix — exactly
   the value written back into UDF_Data.$.RiskScore.

   In ICBC_DEMO the table already exists (legacy heap). This migration:
     • Creates it with the legacy column shape ONLY if missing (fresh DBs).
     • Adds a UNIQUE index on Transaction_ID so the poller can upsert cleanly.

   Legacy column shape (verbatim, all score columns are varchar, no surrogate key):
     Brains_Score varchar(100), Final_Score varchar(100), FCCM_Score varchar(100),
     Transaction_ID int, Display varchar(100), Customer_No varchar(1000)

   Forward-only, idempotent.
   ============================================================================ */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Cstm_Brains', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cstm_Brains
    (
        Brains_Score   VARCHAR(100)  NULL,
        Final_Score    VARCHAR(100)  NULL,
        FCCM_Score     VARCHAR(100)  NULL,
        Transaction_ID INT           NULL,
        Display        VARCHAR(100)  NULL,
        Customer_No    VARCHAR(1000) NULL
    );
END
GO

-- One comparison row per transaction; lets the poller upsert by Transaction_ID.
-- (Filtered to non-NULL so any pre-existing legacy NULL rows don't block it.)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_Cstm_Brains_Transaction_ID'
                 AND object_id = OBJECT_ID('dbo.Cstm_Brains'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Cstm_Brains_Transaction_ID
        ON dbo.Cstm_Brains (Transaction_ID)
        WHERE Transaction_ID IS NOT NULL;
GO
