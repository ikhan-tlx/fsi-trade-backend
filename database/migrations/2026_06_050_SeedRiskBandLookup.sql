-- =========================================================================
-- 2026_06_050_SeedRiskBandLookup.sql
--
-- 2026-06-09 — seed the RISK_BAND lookup that maps the BRAINS v2.0 risk
-- band LABEL (shown to users) to its numeric risk score (stored on the
-- transaction's customer snapshot UDF_Data). This keeps the persisted
-- risk score DECIMAL — matching the legacy domain — while the API / UI
-- speak the human band vocabulary. The lookup is the single source of
-- truth for the band -> number mapping.
--
--   Lookup_Type   Visible_Value   Hidden_Value
--   -----------   -------------   ------------
--   RISK_BAND     PEP             0
--   RISK_BAND     High            1
--   RISK_BAND     Medium          2
--   RISK_BAND     Low             3
--
-- Hidden_Value follows the BRAINS v2.0 STATUS dictionary (0 PEP / 1 High /
-- 2 Medium / 3 Low) — the same scheme the dashboard RiskBandClassifier
-- numeric branch already reads, so stored values classify correctly.
-- (The legacy CBS/BRAINS v1.0 scheme was 1=Low/2=Medium/3=High/4=not-
-- calculated and had no PEP band; we standardise on v2.0.)
--
-- Consumed by CreateTransactionCommandHandler: it receives the band label
-- from the wizard, resolves Hidden_Value here, and stores the NUMBER on
-- TmX_Customer_Master.UDF_Data as { "RiskScore": <n> }.
--
-- Idempotent: every row guarded by NOT EXISTS on (Lookup_Type, Hidden_Value).
-- Safe to re-run. Tenant_ID / Locale_ID derived from any sibling row.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @localeId int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @localeId IS NULL SET @localeId = 1;

DECLARE @now            datetime      = GETUTCDATE();
DECLARE @nowAsNvarchar  nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who            nvarchar(100) = N'System (RISK_BAND lookup seed)';

PRINT CONCAT('Seeding RISK_BAND lookup with Tenant_ID=', @tenantId,
             ', Locale_ID=', @localeId);

DECLARE @seed TABLE (
    Lookup_Type   nvarchar(100),
    Visible_Value nvarchar(999),
    Hidden_Value  nvarchar(100),
    Description   nvarchar(500),
    Sort_Order    int
);

-- Sort_Order ascending by risk severity (PEP highest concern first), to
-- match the BRAINS STATUS numbering.
INSERT INTO @seed VALUES
    (N'RISK_BAND', N'PEP',    N'0', N'Politically Exposed Person — highest-concern risk band.', 10),
    (N'RISK_BAND', N'High',   N'1', N'High risk.',                                              20),
    (N'RISK_BAND', N'Medium', N'2', N'Medium risk.',                                            30),
    (N'RISK_BAND', N'Low',    N'3', N'Low risk.',                                               40);

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID,    Lookup_Type,     Visible_Value,   Hidden_Value,   Description,
     Is_Active,    Active_Flag,     User_Editable,   Sort_Order,
     Lookup_Name,
     Locale_ID,    Locale_Label,
     Created_By,   Created_Date,
     Last_Updated_By, Last_Updated_Date)
SELECT
    @tenantId,    s.Lookup_Type,   s.Visible_Value, s.Hidden_Value, s.Description,
    1,            1,               0,               s.Sort_Order,
    s.Visible_Value,
    @localeId,    s.Visible_Value,
    @who,         @nowAsNvarchar,
    @who,         @now
FROM @seed s
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.TmX_Lookup l
    WHERE l.Lookup_Type  = s.Lookup_Type
      AND l.Hidden_Value = s.Hidden_Value
);

PRINT CONCAT('RISK_BAND seed: inserted ', @@ROWCOUNT, ' new lookup row(s).');

PRINT '';
PRINT 'Current RISK_BAND roster:';
SELECT  Lookup_Type,
        Visible_Value,
        Hidden_Value,
        Sort_Order,
        Is_Active
FROM    dbo.TmX_Lookup
WHERE   Lookup_Type = 'RISK_BAND'
ORDER   BY Sort_Order, Lookup_ID;

GO

PRINT 'RISK_BAND lookup seed complete.';
