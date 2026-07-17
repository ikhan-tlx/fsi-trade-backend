/* 2026_07_013_CustomerInsightIndexes.sql
   Additive, non-breaking performance indexes for the Customer Intelligence
   aggregation (listing enrichment + InsightSummary + InsightDetail).
   Idempotent: each index is created only if it does not already exist.
   SET options are required for CREATE INDEX; kept in a single batch. */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TmX_Transaction_Flag_SetDate'
               AND object_id = OBJECT_ID('dbo.TmX_Transaction_Flag'))
    CREATE INDEX IX_TmX_Transaction_Flag_SetDate
        ON dbo.TmX_Transaction_Flag (Set_Date, Is_Flagged);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_KycCaseRequest_Customer_Status'
               AND object_id = OBJECT_ID('dbo.KycCaseRequest'))
    CREATE INDEX IX_KycCaseRequest_Customer_Status
        ON dbo.KycCaseRequest (Customer_Id, Status);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TmX_Customer_Review_Schedule_NextReview'
               AND object_id = OBJECT_ID('dbo.TmX_Customer_Review_Schedule'))
    CREATE INDEX IX_TmX_Customer_Review_Schedule_NextReview
        ON dbo.TmX_Customer_Review_Schedule (Next_Review_Date, Status);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TmX_Customer_Master_Code'
               AND object_id = OBJECT_ID('dbo.TmX_Customer_Master'))
    CREATE INDEX IX_TmX_Customer_Master_Code
        ON dbo.TmX_Customer_Master (Customer_Code);
