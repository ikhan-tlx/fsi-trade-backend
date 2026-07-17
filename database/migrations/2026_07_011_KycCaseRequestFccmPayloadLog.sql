/* ============================================================================
   2026_07_011_KycCaseRequestFccmPayloadLog
   ----------------------------------------------------------------------------
   Adds diagnostics columns to dbo.KycCaseRequest so support can see the EXACT
   FCCM exchange per case and root-cause a stuck/failed/timeout case:

     • Fccm_Request_Payload  NVARCHAR(MAX) — the exact onboarding request body
                                             last sent to FCCM.
     • Fccm_Response_Payload NVARCHAR(MAX) — append-only, timestamped trail:
                                             submit HTTP status+body, then each
                                             CASE_ID / RISK poll outcome + timeout.

   Additive + idempotent (guarded by COL_LENGTH). Forward-only.
   ============================================================================ */

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.KycCaseRequest', 'Fccm_Request_Payload') IS NULL
    ALTER TABLE dbo.KycCaseRequest ADD Fccm_Request_Payload NVARCHAR(MAX) NULL;
GO

IF COL_LENGTH('dbo.KycCaseRequest', 'Fccm_Response_Payload') IS NULL
    ALTER TABLE dbo.KycCaseRequest ADD Fccm_Response_Payload NVARCHAR(MAX) NULL;
GO
