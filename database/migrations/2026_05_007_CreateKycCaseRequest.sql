-- =========================================================================
-- 2026_05_007_CreateKycCaseRequest.sql
--
-- Slice 4 — schema for the FCCM async KYC-case submission flow.
-- Tracks each KYC submission from "AwaitingCaseId" to a terminal state.
-- Polled in the background by FccmCaseIdPoller (BackgroundService) every
-- few seconds, replacing the legacy 20-second blocking poll inside
-- caseInsertionController.GetCaseIdByRequestId.
--
-- Run on:  ICBC_DEMO
-- Idempotent — guarded by IF NOT EXISTS.
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

IF OBJECT_ID('dbo.KycCaseRequest', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.KycCaseRequest
    (
        Request_Id        bigint        IDENTITY(1,1) NOT NULL CONSTRAINT PK_KycCaseRequest PRIMARY KEY,

        -- Caller / business context
        Tenant_Id         int           NOT NULL,
        Customer_Id       nvarchar(100) NOT NULL,
        Transaction_Id    bigint        NULL,             -- optional link to TmX_Transaction
        Submitted_By      nvarchar(100) NOT NULL,
        Submitted_At      datetime      NOT NULL CONSTRAINT DF_KycCaseRequest_Submitted_At DEFAULT (SYSUTCDATETIME()),

        -- FCCM bridge
        Fccm_Request_Id   nvarchar(100) NULL,             -- response from POST KYCOnboardingURL
        Fccm_Case_Id      nvarchar(100) NULL,             -- populated by poller from FCC_OB_RA.CASE_ID
        Risk_Category_Key nvarchar(50)  NULL,             -- populated by poller from FCC_OB_RA.RISK_CATEGORY_KEY

        -- State machine
        -- Statuses: Submitted | AwaitingCaseId | CaseCreated | RiskAssessed | Failed | Timeout
        Status            nvarchar(50)  NOT NULL CONSTRAINT DF_KycCaseRequest_Status DEFAULT ('Submitted'),
        Last_Polled_At    datetime      NULL,
        Error_Detail      nvarchar(1000) NULL,

        -- Audit
        Last_Updated_At   datetime      NOT NULL CONSTRAINT DF_KycCaseRequest_Last_Updated_At DEFAULT (SYSUTCDATETIME())
    );

    -- Hot-path index: poller selects rows where Status IN ('Submitted', 'AwaitingCaseId', 'CaseCreated').
    CREATE INDEX IX_KycCaseRequest_Status
        ON dbo.KycCaseRequest (Status)
        INCLUDE (Fccm_Request_Id, Submitted_At);

    -- Lookup index: FE polls by Request_Id (PK already covers this).

    -- Customer-history index: useful for "show me all KYC submissions for this customer".
    CREATE INDEX IX_KycCaseRequest_Customer_Id
        ON dbo.KycCaseRequest (Customer_Id, Submitted_At);

    PRINT 'Created table dbo.KycCaseRequest with two supporting indexes.';
END
ELSE
BEGIN
    PRINT 'Table dbo.KycCaseRequest already exists — no changes made.';
END
GO

PRINT 'Slice 4 KYC case schema complete.';
