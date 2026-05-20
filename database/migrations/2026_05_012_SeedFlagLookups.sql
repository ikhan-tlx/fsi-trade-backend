-- =========================================================================
-- 2026_05_012_SeedFlagLookups.sql
--
-- Slice 8 Step 2 — seed the five TmX_Lookup types that the Flag and
-- Document tables reference by FK:
--
--   FLAG_TYPE          Manual, Automated
--   FLAG_SEVERITY      Critical, High, Medium, Low, Info
--   FLAG_CATEGORY      TBML, KYC, Onboarding, Generic
--   FLAG_CHANGE_TYPE   Set, Cleared, Evidence_Attached,
--                      Evidence_Removed, Notes_Updated
--   STORAGE_PROVIDER   LocalDisk, AzureBlob, S3
--                      — drives TmX_Document.Storage_Provider_Lkp_ID
--
-- Idempotent: every row guarded by NOT EXISTS on (Lookup_Type, Hidden_Value).
-- Safe to re-run; new rows added in subsequent edits will be picked up
-- without disturbing existing ones.
--
-- Tenant_ID / Locale_ID are derived from any sibling row already in
-- TmX_Lookup so the seed sits naturally alongside existing reference
-- data; falls back to (1, 1) when the table is otherwise empty.
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
DECLARE @who            nvarchar(100) = N'System (Slice 8 flag-lookup seed)';

PRINT CONCAT('Seeding FLAG_* lookup types with Tenant_ID=', @tenantId,
             ', Locale_ID=', @localeId);

-- A staging table so we can declare all (LookupType, VisibleValue, HiddenValue,
-- Description, SortOrder) rows in one place then loop into the real INSERT
-- with idempotency guards.
DECLARE @seed TABLE (
    Lookup_Type   nvarchar(100),
    Visible_Value nvarchar(999),
    Hidden_Value  nvarchar(100),
    Description   nvarchar(500),
    Sort_Order    int
);

-- -------- FLAG_TYPE --------
-- "Manual" covers analyst-driven flags from the form (the MRL set).
-- "Automated" covers flags raised by upstream systems (BRAINS / FCCM)
-- that the catalogue surfaces in the same UI for unified handling.
INSERT INTO @seed VALUES
    (N'FLAG_TYPE', N'Manual',    N'MANUAL',    N'Analyst-set on the transaction form.', 10),
    (N'FLAG_TYPE', N'Automated', N'AUTOMATED', N'Raised by an upstream system (BRAINS, FCCM, sanctions).', 20);

-- -------- FLAG_SEVERITY --------
-- Five-level scale; Default_Weight on the catalogue lets admins tune
-- the actual numeric contribution per flag.
INSERT INTO @seed VALUES
    (N'FLAG_SEVERITY', N'Critical', N'CRITICAL', N'Severe risk — typically requires escalation/blocking.',  10),
    (N'FLAG_SEVERITY', N'High',     N'HIGH',     N'Significant risk — review and document before approval.', 20),
    (N'FLAG_SEVERITY', N'Medium',   N'MEDIUM',   N'Moderate risk — review with normal SLA.',                 30),
    (N'FLAG_SEVERITY', N'Low',      N'LOW',      N'Minor risk — note in audit trail.',                       40),
    (N'FLAG_SEVERITY', N'Info',     N'INFO',     N'Informational only — no action required.',               50);

-- -------- FLAG_CATEGORY --------
-- The MRL migration seeds TBML for trade products (Import / Export / LG)
-- and KYC for the KYC product (5). Onboarding and Generic are forward-
-- looking; useful as soon as we add more flag sources.
INSERT INTO @seed VALUES
    (N'FLAG_CATEGORY', N'TBML',       N'TBML',       N'Trade-Based Money Laundering indicator.',    10),
    (N'FLAG_CATEGORY', N'KYC',        N'KYC',        N'KYC / Customer Due Diligence indicator.',    20),
    (N'FLAG_CATEGORY', N'Onboarding', N'ONBOARDING', N'Customer onboarding red flag.',              30),
    (N'FLAG_CATEGORY', N'Generic',    N'GENERIC',    N'General compliance flag — uncategorised.',   40);

-- -------- FLAG_CHANGE_TYPE --------
-- Drives the discriminator on TmX_Transaction_Flag_History.
-- "Set" / "Cleared" cover boolean toggles. The remaining three split out
-- evidence and notes edits so audit consumers can filter cleanly.
INSERT INTO @seed VALUES
    (N'FLAG_CHANGE_TYPE', N'Set',                N'SET',                N'Flag was toggled on.',                            10),
    (N'FLAG_CHANGE_TYPE', N'Cleared',            N'CLEARED',            N'Flag was toggled off.',                           20),
    (N'FLAG_CHANGE_TYPE', N'Evidence Attached',  N'EVIDENCE_ATTACHED',  N'Supporting document linked to the flag.',         30),
    (N'FLAG_CHANGE_TYPE', N'Evidence Removed',   N'EVIDENCE_REMOVED',   N'Supporting document unlinked from the flag.',     40),
    (N'FLAG_CHANGE_TYPE', N'Notes Updated',      N'NOTES_UPDATED',      N'Analyst note text changed (no flag-state shift).', 50);

-- -------- STORAGE_PROVIDER --------
-- Drives TmX_Document.Storage_Provider_Lkp_ID. LOCAL_DISK is the only
-- one used at deploy-time today (files under ICBC_Data on the deployed
-- server). The other two are seeded for forward-compat so adopting cloud
-- storage later is a config flip rather than a schema change.
INSERT INTO @seed VALUES
    (N'STORAGE_PROVIDER', N'Local Disk',  N'LOCAL_DISK', N'Files stored on the deployed server filesystem (e.g. ICBC_Data folder).', 10),
    (N'STORAGE_PROVIDER', N'Azure Blob',  N'AZURE_BLOB', N'Files stored in an Azure Blob Storage container.',                        20),
    (N'STORAGE_PROVIDER', N'Amazon S3',   N'S3',         N'Files stored in an AWS S3 bucket.',                                       30);

-- Idempotent insert: only rows that don't already exist (by Lookup_Type
-- + Hidden_Value) get inserted.
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

PRINT CONCAT('FLAG_* seed: inserted ', @@ROWCOUNT, ' new lookup row(s).');

PRINT '';
PRINT 'Current FLAG_* / STORAGE_PROVIDER roster:';
SELECT  Lookup_Type,
        Visible_Value,
        Hidden_Value,
        Sort_Order,
        Is_Active
FROM    dbo.TmX_Lookup
WHERE   Lookup_Type IN ('FLAG_TYPE', 'FLAG_SEVERITY', 'FLAG_CATEGORY',
                        'FLAG_CHANGE_TYPE', 'STORAGE_PROVIDER')
ORDER   BY Lookup_Type, Sort_Order, Lookup_ID;

GO

PRINT 'Slice 8 Step 2 — FLAG_* and STORAGE_PROVIDER lookup seed complete.';
