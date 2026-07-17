/* ============================================================================
   2026_07_006_CustomerReviewSchedule
   ----------------------------------------------------------------------------
   Periodic Customer Review — the per-customer schedule of record.

   One row PER CUSTOMER (keyed by Customer_Code) holding the current risk
   category, the computed Next_Review_Date, and a status. This is a dedicated
   table (NOT a column on the per-transaction snapshot TmX_Customer_Master).

   The PeriodicReviewService scans WHERE Status='Scheduled' AND Next_Review_Date<=now
   and claims a cycle by an atomic UPDATE Status 'Scheduled'->'InReview'. The FCCM
   callback upserts this row on any terminal KYC decision (approve or reject).

   Status: Scheduled | InReview | Blocked | Failed | Suspended.

   Additive, forward-only, idempotent. Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.TmX_Customer_Review_Schedule', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Customer_Review_Schedule
    (
        Customer_Review_Schedule_Id INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Customer_Review_Schedule PRIMARY KEY,
        Tenant_Id               INT            NOT NULL CONSTRAINT DF_CRS_Tenant DEFAULT (1),
        Customer_Code           NVARCHAR(50)   NOT NULL,
        Current_Risk_Category   NVARCHAR(20)   NULL,
        Current_Risk_Score      INT            NULL,
        Next_Review_Date        DATETIME2      NULL,
        Status                  NVARCHAR(30)   NOT NULL CONSTRAINT DF_CRS_Status DEFAULT ('Scheduled'),
        Last_Reviewed_Date      DATETIME2      NULL,
        Last_Kyc_Transaction_Id INT            NULL,
        Last_Remark             NVARCHAR(1000) NULL,
        Created_By              NVARCHAR(100)  NOT NULL CONSTRAINT DF_CRS_CreatedBy DEFAULT ('PERIODIC REVIEWER'),
        Created_Date            DATETIME2      NOT NULL CONSTRAINT DF_CRS_CreatedDate DEFAULT (SYSUTCDATETIME()),
        Last_Updated_By         NVARCHAR(100)  NULL,
        Last_Updated_Date       DATETIME2      NULL
    );
END
GO

-- One schedule per customer (also the upsert key).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_CRS_Tenant_Customer'
                 AND object_id = OBJECT_ID('dbo.TmX_Customer_Review_Schedule'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_CRS_Tenant_Customer
        ON dbo.TmX_Customer_Review_Schedule (Tenant_Id, Customer_Code);
GO

-- The due-scan: Scheduled + due date.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_CRS_Status_NextReview'
                 AND object_id = OBJECT_ID('dbo.TmX_Customer_Review_Schedule'))
    CREATE NONCLUSTERED INDEX IX_CRS_Status_NextReview
        ON dbo.TmX_Customer_Review_Schedule (Tenant_Id, Status, Next_Review_Date);
GO
