/* ============================================================================
   2026_07_007_BackfillCustomerReviewSchedule
   ----------------------------------------------------------------------------
   One-time backfill of dbo.TmX_Customer_Review_Schedule from each customer's
   latest TERMINAL (Approved or Rejected) KYC, using their current reconciled
   risk. Seeds one 'Scheduled' row per customer that has a terminal KYC with a
   resolvable risk.

   Risk resolution (numeric BRAINS scale 0 PEP / 1 High / 2 Medium / 3 Low),
   first non-null of:
     1. Cstm_Brains.Final_Score           (reconciled higher-risk-wins)
     2. KycCaseRequest.Risk_Category_Key  (if numeric)
     3. TmX_Customer_Master.udf_data.$.RiskScore (create-time BRAINS band)
   The band LABEL (Current_Risk_Category) is derived from the numeric score via
   the RISK_BAND lookup. Customers with no resolvable risk get NO row.

   Idempotent: only inserts customers not already scheduled (NOT EXISTS).
   Status filter is case-insensitive (UPPER) — the env stores APPROVED/REJECTED.

   Anchor = HistoricalSmoothed: historical due date, but if already past, stagger
   over @Smoothing days (@Now + ABS(CHECKSUM(code)) % N) so go-live does not fire
   a burst of onboardings. The band->months map mirrors appsettings
   PeriodicReview:Frequencies; keep in sync if you change frequencies.

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ============================================================================
   PRE-FLIGHT (READ-ONLY) — review coverage BEFORE the backfill INSERT below.
   Nothing is modified here. Run just this batch first to confirm the terminal-
   status set + risk sources cover the customers you expect. It uses the SAME
   product + terminal-status filter as the INSERT, so the numbers are exactly
   what the backfill will act on.
   ============================================================================ */
DECLARE @PfTenant INT = 1;
DECLARE @PfKycProductId INT = (SELECT TOP 1 Product_ID FROM dbo.TmX_Product WHERE Product_Name = N'KYC');

PRINT '--- KYC transactions by status (In_Backfill = counted as terminal by this migration) ---';
SELECT s.Visible_Value                 AS Status,
       COUNT(*)                        AS Kyc_Txns,
       COUNT(DISTINCT cm.Customer_Code) AS Distinct_Customers,
       CASE WHEN UPPER(s.Visible_Value) IN (N'APPROVED', N'REJECTED') THEN N'YES' ELSE N'no' END AS In_Backfill_Terminal_Set
FROM       dbo.TmX_Transaction     t
JOIN       dbo.TmX_Customer_Master cm ON cm.Transaction_Id = t.Transaction_Id
JOIN       dbo.TmX_Lookup          s  ON s.Lookup_ID = t.Transaction_Status_Lkp
                                     AND s.Lookup_Type = N'APPLICATION_STATUS'
WHERE      t.Product_Id = @PfKycProductId
GROUP BY   s.Visible_Value
ORDER BY   COUNT(*) DESC;

PRINT '--- backfill coverage summary (how many customers the INSERT will seed vs skip) ---';
;WITH terminal_kyc AS (
    SELECT cm.Customer_Code,
           TRY_CONVERT(INT, cb.Final_Score)                         AS s_cb,
           TRY_CONVERT(INT, kc.Risk_Category_Key)                   AS s_kc,
           TRY_CONVERT(INT, JSON_VALUE(cm.udf_data, '$.RiskScore')) AS s_udf,
           ROW_NUMBER() OVER (PARTITION BY cm.Customer_Code ORDER BY t.Transaction_Id DESC) rn
    FROM       dbo.TmX_Transaction      t
    JOIN       dbo.TmX_Customer_Master  cm ON cm.Transaction_Id = t.Transaction_Id
    JOIN       dbo.TmX_Lookup           s  ON s.Lookup_ID = t.Transaction_Status_Lkp
                                          AND s.Lookup_Type = N'APPLICATION_STATUS'
                                          AND UPPER(s.Visible_Value) IN (N'APPROVED', N'REJECTED')
    LEFT JOIN  dbo.Cstm_Brains    cb ON cb.Transaction_ID = t.Transaction_Id
    LEFT JOIN  dbo.KycCaseRequest kc ON kc.Transaction_Id = t.Transaction_Id
    WHERE  t.Product_Id = @PfKycProductId AND cm.Customer_Code IS NOT NULL
),
latest AS (
    SELECT Customer_Code, COALESCE(s_cb, s_kc, s_udf) AS Risk_Score
    FROM terminal_kyc WHERE rn = 1
)
SELECT
  (SELECT COUNT(DISTINCT Customer_Code) FROM dbo.TmX_Customer_Master WHERE Customer_Code IS NOT NULL) AS Total_KYC_Customers,
  (SELECT COUNT(*) FROM latest)                                  AS With_Terminal_KYC,
  (SELECT COUNT(*) FROM latest WHERE Risk_Score IS NOT NULL)     AS Terminal_And_Risk_Resolvable,
  (SELECT COUNT(*) FROM latest WHERE Risk_Score IS NULL)         AS Terminal_But_No_Risk_Skipped,
  (SELECT COUNT(*) FROM latest l WHERE l.Risk_Score IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Customer_Review_Schedule sch
                       WHERE sch.Tenant_Id = @PfTenant AND sch.Customer_Code = l.Customer_Code)) AS Will_Be_Seeded_Now,
  (SELECT COUNT(*) FROM dbo.TmX_Customer_Review_Schedule WHERE Tenant_Id = @PfTenant)             AS Already_Scheduled;
GO

DECLARE @Tenant INT = 1, @Now DATETIME2 = SYSUTCDATETIME(), @Smoothing INT = 30;
DECLARE @KycProductId INT = (SELECT TOP 1 Product_ID FROM dbo.TmX_Product WHERE Product_Name = N'KYC');

;WITH terminal_kyc AS (          -- latest terminal (Approved/Rejected) KYC per customer + candidate risk sources
    SELECT cm.Customer_Code, t.Transaction_Id, t.Last_Updated_Date AS Completed_Date,
           TRY_CONVERT(INT, cb.Final_Score)                         AS s_cb,
           TRY_CONVERT(INT, kc.Risk_Category_Key)                   AS s_kc,
           TRY_CONVERT(INT, JSON_VALUE(cm.udf_data, '$.RiskScore')) AS s_udf,
           ROW_NUMBER() OVER (PARTITION BY cm.Customer_Code ORDER BY t.Transaction_Id DESC) rn
    FROM       dbo.TmX_Transaction      t
    JOIN       dbo.TmX_Customer_Master  cm ON cm.Transaction_Id = t.Transaction_Id
    JOIN       dbo.TmX_Lookup           s  ON s.Lookup_ID = t.Transaction_Status_Lkp
                                          AND s.Lookup_Type = N'APPLICATION_STATUS'
                                          AND UPPER(s.Visible_Value) IN (N'APPROVED', N'REJECTED')
    LEFT JOIN  dbo.Cstm_Brains    cb ON cb.Transaction_ID = t.Transaction_Id
    LEFT JOIN  dbo.KycCaseRequest kc ON kc.Transaction_Id = t.Transaction_Id
    WHERE  t.Product_Id = @KycProductId AND cm.Customer_Code IS NOT NULL
),
latest AS (
    SELECT Customer_Code, Transaction_Id, Completed_Date,
           COALESCE(s_cb, s_kc, s_udf) AS Risk_Score
    FROM terminal_kyc WHERE rn = 1
),
freq(Score, Months) AS (         -- mirrors appsettings PeriodicReview:Frequencies (BRAINS scale)
    SELECT * FROM (VALUES (0,1),(1,1),(2,2),(3,3)) f(Score, Months)   -- PEP/High=1, Medium=2, Low=3
)
INSERT dbo.TmX_Customer_Review_Schedule
   (Tenant_Id, Customer_Code, Current_Risk_Category, Current_Risk_Score,
    Next_Review_Date, Status, Last_Reviewed_Date, Last_Kyc_Transaction_Id,
    Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT @Tenant, l.Customer_Code, rb.Visible_Value, l.Risk_Score,
       CASE WHEN DATEADD(MONTH, COALESCE(fr.Months,3), l.Completed_Date) <= @Now
            THEN DATEADD(DAY, ABS(CHECKSUM(l.Customer_Code)) % @Smoothing, @Now)   -- smooth overdue
            ELSE DATEADD(MONTH, COALESCE(fr.Months,3), l.Completed_Date) END,
       N'Scheduled', l.Completed_Date, l.Transaction_Id,
       N'PERIODIC REVIEWER', @Now, N'PERIODIC REVIEWER', @Now
FROM       latest l
LEFT JOIN  freq fr ON fr.Score = l.Risk_Score
LEFT JOIN  dbo.TmX_Lookup rb ON rb.Lookup_Type = N'RISK_BAND' AND rb.Tenant_ID = @Tenant
                            AND TRY_CONVERT(INT, rb.Hidden_Value) = l.Risk_Score
WHERE l.Risk_Score IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Customer_Review_Schedule sch
                  WHERE sch.Tenant_Id = @Tenant AND sch.Customer_Code = l.Customer_Code);
GO
