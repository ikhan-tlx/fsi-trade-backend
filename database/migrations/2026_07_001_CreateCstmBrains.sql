/* ============================================================================
   2026_07_001_CreateCstmBrains
   ----------------------------------------------------------------------------
   Ports the legacy dbo.Cstm_Brains comparison table used by the old
   caseInsertionController risk pipeline. It records, per transaction, the FCCM
   (TBML) band vs the BRAINS band and the reconciled "higher-risk wins" outcome
   plus its attribution suffix — exactly the value written back into
   UDF_Data.$.RiskScore.

   Legacy columns (preserved): FCCM_Score, Brains_Score, Customer_No,
   Transaction_ID, Final_Score, Display. We add a surrogate Id PK and audit
   timestamps, and a UNIQUE index on Transaction_ID so the poller can upsert.

   Forward-only, idempotent.
   ============================================================================ */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Cstm_Brains', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cstm_Brains
    (
        Id               BIGINT          IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_Cstm_Brains PRIMARY KEY,
        Transaction_ID   BIGINT          NOT NULL,
        Customer_No      NVARCHAR(100)   NULL,
        FCCM_Score       INT             NULL,
        Brains_Score     INT             NULL,
        Final_Score      NVARCHAR(20)    NULL,
        Display          NVARCHAR(60)     NULL,
        Created_At       DATETIME2(3)    NOT NULL
            CONSTRAINT DF_Cstm_Brains_Created_At   DEFAULT (SYSUTCDATETIME()),
        Last_Updated_At  DATETIME2(3)    NOT NULL
            CONSTRAINT DF_Cstm_Brains_Updated_At   DEFAULT (SYSUTCDATETIME())
    );
END
GO

-- One comparison row per transaction; lets the poller upsert by Transaction_ID.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_Cstm_Brains_Transaction_ID'
                 AND object_id = OBJECT_ID('dbo.Cstm_Brains'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Cstm_Brains_Transaction_ID
        ON dbo.Cstm_Brains (Transaction_ID);
GO
