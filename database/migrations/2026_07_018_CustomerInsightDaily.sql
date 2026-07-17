/* 2026_07_018_CustomerInsightDaily.sql
   ---------------------------------------------------------------------------
   Customer Intelligence daily snapshot table (plan §6 opt-1).

   One book-wide row PER DAY capturing the portfolio KPIs the listing strip shows
   (total customers, high-risk, reviews-overdue, open cases, flags-7d). The daily
   CustomerInsightSnapshotService upserts today's row; GET /Customer/InsightSummary
   reads the row from ~7 days ago to compute REAL "vs last week" deltas.

   Additive + isolated — no existing table is touched. Idempotent.
   ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.TmX_Customer_Insight_Daily', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Customer_Insight_Daily
    (
        Customer_Insight_Daily_Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Customer_Insight_Daily PRIMARY KEY,
        Snapshot_Date     DATE          NOT NULL,
        Total_Customers   INT           NOT NULL CONSTRAINT DF_TmX_CID_TotalCustomers DEFAULT (0),
        High_Risk         INT           NOT NULL CONSTRAINT DF_TmX_CID_HighRisk       DEFAULT (0),
        Reviews_Overdue   INT           NOT NULL CONSTRAINT DF_TmX_CID_ReviewsOverdue DEFAULT (0),
        Open_Cases        INT           NOT NULL CONSTRAINT DF_TmX_CID_OpenCases      DEFAULT (0),
        Flags_Raised_7d   INT           NOT NULL CONSTRAINT DF_TmX_CID_Flags7d        DEFAULT (0),
        Created_By        NVARCHAR(100) NOT NULL CONSTRAINT DF_TmX_CID_CreatedBy      DEFAULT ('CUSTOMER_INSIGHT_SNAPSHOT'),
        Created_Date      DATETIME2(7)  NOT NULL CONSTRAINT DF_TmX_CID_CreatedDate    DEFAULT (SYSUTCDATETIME()),
        Last_Updated_Date DATETIME2(7)  NULL
    );
    PRINT 'Created dbo.TmX_Customer_Insight_Daily.';
END
ELSE
    PRINT 'dbo.TmX_Customer_Insight_Daily already exists — skipped.';
GO

/* One row per calendar day (book-wide) — the upsert key. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_TmX_Customer_Insight_Daily_Snapshot_Date'
                 AND object_id = OBJECT_ID('dbo.TmX_Customer_Insight_Daily'))
BEGIN
    CREATE UNIQUE INDEX UX_TmX_Customer_Insight_Daily_Snapshot_Date
        ON dbo.TmX_Customer_Insight_Daily (Snapshot_Date);
    PRINT 'Created unique index UX_TmX_Customer_Insight_Daily_Snapshot_Date.';
END
ELSE
    PRINT 'UX_TmX_Customer_Insight_Daily_Snapshot_Date already exists — skipped.';
GO
