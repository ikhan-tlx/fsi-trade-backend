-- =========================================================================
-- 2026_07_021_SeedEsiKycTab.sql
--
-- 2026-07-13 — ESI — part 3 of 4.
-- Adds the metadata-driven Economic Substance (ESI) tab to the KYC product:
--   TmX_Tab (tab) + TmX_Entity_Tab_Product_Mapping (KYC->tab)
--   + TmX_Tenant_Field_Setup: section headers (type 30), a summary control
--     (role=summary), DropdownQuestion checkpoints (type 66 -> ESI_YES_NO_NA,
--     per-field highRisk in UDF_Data), and a justification textarea (type 4).
--
-- 100% config: adding/removing a checkpoint = one row; the FE renders N x M
-- from metadata with no code change.
--
-- Prereqs: run 019 (ESI_YES_NO_NA lookup) and 020 (DropdownQuestion type) first.
-- Idempotent: guarded by NOT EXISTS on Field_Name within the ESI tab.
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @now    datetime      = GETUTCDATE();
DECLARE @who    nvarchar(100) = N'System (ESI KYC tab seed)';

DECLARE @kycProductId int = (SELECT TOP 1 Product_Id FROM dbo.TmX_Product WHERE Product_Name = N'KYC');
IF @kycProductId IS NULL BEGIN PRINT 'KYC product not found — abort.'; RETURN; END

DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Tab ORDER BY Tab_Id);
DECLARE @localeId int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Tab ORDER BY Tab_Id);
IF @tenantId IS NULL SET @tenantId = 1;
IF @localeId IS NULL SET @localeId = 1;

-- Control type for checkpoints: DropdownQuestion (code = its FIELD Hidden_Value);
-- falls back to plain Dropdown (25) if 020 has not been run.
DECLARE @dqType int = (SELECT TOP 1 TRY_CAST(Hidden_Value AS int)
                       FROM dbo.TmX_Lookup WHERE Lookup_Type = N'FIELD' AND Visible_Value = N'DropdownQuestion');
IF @dqType IS NULL SET @dqType = 25;

-- Field_Table_Name routes a field's answer into the right UDF blob. Derive the KYC
-- form's real convention from an existing KYC field (robust across environments);
-- fall back to the documented 'TmxTransactionDetail[]' (see 2026_05_013).
DECLARE @tblName nvarchar(100) = (
    SELECT TOP 1 Field_Table_Name FROM dbo.TmX_Tenant_Field_Setup
    WHERE Product_Id = @kycProductId AND Field_Table_Name IS NOT NULL
      AND Field_Table_Name LIKE N'%TransactionDetail%' ORDER BY Tenant_Field_Setup_Id);
IF @tblName IS NULL SET @tblName = N'TmxTransactionDetail[]';
PRINT CONCAT('ESI answers will persist under Field_Table_Name = ', @tblName);

-- ---- 1. Tab -------------------------------------------------------------
DECLARE @esiTabId int = (SELECT TOP 1 Tab_Id FROM dbo.TmX_Tab WHERE Tab_Name = N'Economic Substance');
IF @esiTabId IS NULL
BEGIN
    INSERT INTO dbo.TmX_Tab (Tab_Name, Description, Hidden_Value, Tenant_ID, Active_Flag,
                             Locale_Label, Locale_ID, Created_By, Created_Date)
    VALUES (N'Economic Substance', N'Economic Substance Indicator (ESI) questionnaire.',
            N'ESI', @tenantId, 1, N'Economic Substance', @localeId, @who, @now);
    SET @esiTabId = SCOPE_IDENTITY();
END

-- ---- 2. Product<->Tab mapping (after existing KYC tabs) ------------------
DECLARE @entityId int = (SELECT TOP 1 Entity_Id FROM dbo.TmX_Entity_Tab_Product_Mapping
                         WHERE Product_Id = @kycProductId ORDER BY Sort_Order);
DECLARE @nextSort int = (SELECT ISNULL(MAX(Sort_Order),0)+10
                         FROM dbo.TmX_Entity_Tab_Product_Mapping WHERE Product_Id = @kycProductId);

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Entity_Tab_Product_Mapping
               WHERE Product_Id = @kycProductId AND Tab_Id = @esiTabId)
    INSERT INTO dbo.TmX_Entity_Tab_Product_Mapping
        (Tenant_Id, Entity_Id, Product_Id, Tab_Id, Parent_Tab_Id, Is_Active, Is_Visible, Sort_Order, Created_By, Created_Date)
    VALUES (@tenantId, @entityId, @kycProductId, @esiTabId, NULL, 1, 1, @nextSort, @who, @now);

-- ---- 3. Sections + checkpoints (data-driven) ----------------------------
IF OBJECT_ID('tempdb..#esi') IS NOT NULL DROP TABLE #esi;
CREATE TABLE #esi (Kind char(1), SecCode nvarchar(10), Code nvarchar(10),
                   Label nvarchar(400), Seq int, HighRisk nvarchar(20));
-- Kind: S=section, Q=question. HighRisk = JSON array of Hidden_Value tokens.
INSERT INTO #esi VALUES
 ('S','A',NULL,N'A. Physical & Operational Presence',10,NULL),
 ('Q','A','A1',N'A1 - Trade premises suitable for declared goods/services',11,N'["N","NA"]'),
 ('Q','A','A2',N'A2 - Warehouse / storage capacity aligns with trade volume',12,N'["N"]'),
 ('Q','A','A3',N'A3 - Manufacturing / processing capability (if applicable)',13,N'["N","NA"]'),
 ('S','B',NULL,N'B. Human Resource Capacity',20,NULL),
 ('Q','B','B1',N'B1 - Number of employees adequate for trade volume',21,N'["N"]'),
 ('Q','B','B2',N'B2 - Presence of skilled staff relevant to goods/services',22,N'["N"]'),
 ('Q','B','B3',N'B3 - Use of outsourced staff / agents disclosed',23,N'["N"]'),
 ('S','C',NULL,N'C. Financial Capacity vs Trade Activity',30,NULL),
 ('Q','C','C2',N'C2 - Working capital adequacy for trade cycles',31,N'["N"]'),
 ('Q','C','C4',N'C4 - Reliance on third-party funding disclosed',32,N'["N"]'),
 ('S','F',NULL,N'F. Third-Party & Outsourcing Dependency',40,NULL),
 ('Q','F','F1',N'F1 - Use of third-party logistics disclosed',41,N'["N"]'),
 ('Q','F','F3',N'F3 - Agents / intermediaries with unclear role',42,N'["N"]'),
 ('S','G',NULL,N'G. Assessment',50,NULL);

-- 3a. Summary control (role=summary) — position configurable via Field_Sequence.
INSERT INTO dbo.TmX_Tenant_Field_Setup
    (Field_Name, Field_Label, Field_Type_Lkp, Field_Sequence, Parent_Tenant_Field_Setup_Id,
     Product_Id, Tab_Id, Tenant_Id, Is_Active, Is_Mandatory, Visibility, UDF_Data,
     Locale_ID, Locale_Label, Created_By, Created_Date)
SELECT N'ESI_SUMMARY', N'Economic Substance - outcome', 30, 5, NULL,
       @kycProductId, @esiTabId, @tenantId, 1, N'false', N'true',
       N'{"esi":{"role":"summary","sticky":true}}', @localeId, N'Economic Substance', @who, @now
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup f
                  WHERE f.Tab_Id = @esiTabId AND f.Field_Name = N'ESI_SUMMARY');

-- 3b. Sections (type 30).
INSERT INTO dbo.TmX_Tenant_Field_Setup
    (Field_Name, Field_Label, Field_Type_Lkp, Field_Sequence, Parent_Tenant_Field_Setup_Id,
     Product_Id, Tab_Id, Tenant_Id, Is_Active, Is_Mandatory, Visibility, Locale_ID, Locale_Label,
     Created_By, Created_Date)
SELECT CONCAT(N'ESI_SEC_', e.SecCode), e.Label, 30, e.Seq, NULL,
       @kycProductId, @esiTabId, @tenantId, 1, N'false', N'true', @localeId, e.Label, @who, @now
FROM #esi e
WHERE e.Kind = 'S'
  AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup f
                  WHERE f.Tab_Id = @esiTabId AND f.Field_Name = CONCAT(N'ESI_SEC_', e.SecCode));

-- 3c. Checkpoints (DropdownQuestion; bound to ESI_YES_NO_NA; per-field highRisk in UDF_Data).
INSERT INTO dbo.TmX_Tenant_Field_Setup
    (Field_Name, Field_Label, Field_Type_Lkp, Field_Sequence, Parent_Tenant_Field_Setup_Id,
     Product_Id, Tab_Id, Tenant_Id, Field_Lookup_Type, Field_Table_Name, Is_Active, Is_Mandatory, Visibility,
     UDF_Data, Locale_ID, Locale_Label, Created_By, Created_Date)
SELECT CONCAT(N'ESI_', q.Code), q.Label, @dqType, q.Seq,
       (SELECT TOP 1 s.Tenant_Field_Setup_Id FROM dbo.TmX_Tenant_Field_Setup s
        WHERE s.Tab_Id = @esiTabId AND s.Field_Name = CONCAT(N'ESI_SEC_', q.SecCode)),
       @kycProductId, @esiTabId, @tenantId, N'ESI_YES_NO_NA', @tblName, 1, N'true', N'true',
       CONCAT(N'{"esi":{"code":"', q.Code, N'","inCalc":true,"highRisk":', q.HighRisk, N'}}'),
       @localeId, q.Label, @who, @now
FROM #esi q
WHERE q.Kind = 'Q'
  AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup f
                  WHERE f.Tab_Id = @esiTabId AND f.Field_Name = CONCAT(N'ESI_', q.Code));

-- 3d. Justification (textarea type 4). Required only when outcome is High (derived).
INSERT INTO dbo.TmX_Tenant_Field_Setup
    (Field_Name, Field_Label, Field_Type_Lkp, Field_Sequence, Parent_Tenant_Field_Setup_Id,
     Product_Id, Tab_Id, Tenant_Id, Field_Table_Name, Is_Active, Is_Mandatory, Visibility, UDF_Data,
     Locale_ID, Locale_Label, Created_By, Created_Date)
SELECT N'ESI_G3_Justification', N'Justification', 4, 51,
       (SELECT TOP 1 s.Tenant_Field_Setup_Id FROM dbo.TmX_Tenant_Field_Setup s
        WHERE s.Tab_Id = @esiTabId AND s.Field_Name = N'ESI_SEC_G'),
       @kycProductId, @esiTabId, @tenantId, @tblName, 1, N'false', N'true',
       N'{"esi":{"role":"justification","requiredWhen":"high"}}', @localeId, N'Justification', @who, @now
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup f
                  WHERE f.Tab_Id = @esiTabId AND f.Field_Name = N'ESI_G3_Justification');

DROP TABLE #esi;

PRINT CONCAT('ESI KYC tab seeded (Tab_Id=', @esiTabId, ', dqType=', @dqType, ').');
SELECT Field_Name, Field_Type_Lkp, Field_Sequence, Parent_Tenant_Field_Setup_Id, Field_Lookup_Type, UDF_Data
FROM   dbo.TmX_Tenant_Field_Setup WHERE Tab_Id = @esiTabId ORDER BY Field_Sequence;
GO

PRINT 'ESI KYC tab seed complete.';
