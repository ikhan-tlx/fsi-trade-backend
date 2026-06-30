/* ============================================================================
   2026_06_062_ResetAutoFlagsSeedIlfSanctionedCountry.sql

   Fresh start for auto flags:

     1. REMOVE every existing AUTOMATED flag (catalogue + its rules, scopes,
        locales, and any Source='Auto' transaction-flag rows/history it raised).
        Manual flags are untouched.

     2. SEED one new auto flag — "Sanctioned country present" — that screens
        EVERY country field the legacy TradeController checked against the
        sanctioned-countries list, FIELD-WISE across ALL product tabs (ILF, ILA,
        ILL, ICR, ICA, ICD, SG, IOA, IAPP, ID, ICL, IAP). This is the legacy
        behaviour: screening is per field, not per tab.

        Two legacy sources are folded in (both screen REPO_SANCTIONED_COUNTRIES):
          • Populate_High_Risk_Countries(...)    — single-value country fields
                                                   (applicant / bank / drawee /
                                                    place / port-of-discharge…)
          • Populate_High_Risk_Port_Country(...) — multi-value origin / port-of-
                                                   loading country fields.
        HS-code / goods screening (REPO_SCREENING_GOODS) is intentionally NOT
        here — that belongs to a separate "sanctioned goods" flag.

        Each field is its own OR group → field In SANCTIONED_COUNTRIES.
        Raise_Per_Match = 1 → one flag (and one FCCM event) per matching country,
        mirroring the legacy "flag every field, don't exit on first".

   Scope: every product that owns at least one of these fields (resolved from
   TmX_Tenant_Field_Setup), product-level (Tab_ID NULL). Field codes only exist
   on their own product's transactions, so unrelated rules simply never match.

   Idempotent: re-running deletes the seeded flag and recreates it identically.

   Run:
     sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_06_062_ResetAutoFlagsSeedIlfSanctionedCountry.sql"
   ============================================================================ */

USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @autoType int =
    (SELECT Lookup_ID FROM dbo.TmX_Lookup
     WHERE Lookup_Type = 'FLAG_TYPE' AND Hidden_Value = 'AUTOMATED');

IF @autoType IS NULL
BEGIN
    RAISERROR('FLAG_TYPE/AUTOMATED lookup not found — run 2026_05_012 first.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

DECLARE @sev int =
    (SELECT TOP 1 Lookup_ID FROM dbo.TmX_Lookup
     WHERE Lookup_Type = 'FLAG_SEVERITY' AND (Hidden_Value = 'HIGH' OR Visible_Value = 'High')
     ORDER BY Lookup_ID);

/* -- 1. Remove existing AUTOMATED flags + everything that hangs off them ---- */
DECLARE @autoIds TABLE (Flag_ID int PRIMARY KEY);
INSERT INTO @autoIds (Flag_ID)
    SELECT Flag_ID FROM dbo.TmX_Flag_Catalogue WHERE Flag_Type_Lkp_ID = @autoType;

DELETE FROM dbo.TmX_Transaction_Flag_History
    WHERE Flag_ID IN (SELECT Flag_ID FROM @autoIds);
DELETE FROM dbo.TmX_Transaction_Flag
    WHERE Flag_ID IN (SELECT Flag_ID FROM @autoIds);
DELETE FROM dbo.TmX_Flag_Rule
    WHERE Flag_ID IN (SELECT Flag_ID FROM @autoIds);
DELETE FROM dbo.TmX_Flag_Scope
    WHERE Flag_ID IN (SELECT Flag_ID FROM @autoIds);
DELETE FROM dbo.TmX_Flag_Catalogue_Locale
    WHERE Flag_ID IN (SELECT Flag_ID FROM @autoIds);
DELETE FROM dbo.TmX_Flag_Catalogue
    WHERE Flag_ID IN (SELECT Flag_ID FROM @autoIds);

DECLARE @removed int = (SELECT COUNT(*) FROM @autoIds);
PRINT CONCAT('Removed ', @removed, ' existing auto flag(s).');

/* -- 2. The full legacy country-field set (field-wise, all tabs) ----------- */
-- EXACTLY the fields the legacy passes to Populate_High_Risk_Countries(...) —
-- the single-value country fields. The multi-value origin / port-of-loading
-- country fields (Populate_High_Risk_Port_Country) are deliberately NOT here.
DECLARE @fields TABLE (code nvarchar(100) PRIMARY KEY);
INSERT INTO @fields (code) VALUES
  -- ILF (Import LC – Foreign)
  ('ILF_AppCountry'),('ILF_Address_Cntry1'),('ILF_Address_Cntry2'),('ILF_Address_Cntry3'),
  ('ILF_Country_AppBk'),('ILF_Cntry_AdvBk1'),('ILF_Cntry_AdvBk2'),('ILF_Cntry_AdvBk3'),
  ('ILF_Country_NegBk'),('ILF_Country_ReBk'),('ILF_Country_ConBk'),('ILF_Country_Drawee'),
  ('ILF_PlaceCntry'),('ILF_PortDischCtry'),
  -- ILA (Import LC – Amendment)
  ('ILA_AppCountry'),('ILA_Address_Cntry1'),('ILA_Address_Cntry2'),('ILA_Address_Cntry3'),
  ('ILA_Country_AppBk'),('ILA_Cntry_AdvBk1'),('ILA_Cntry_AdvBk2'),('ILA_Cntry_AdvBk3'),
  ('ILA_Country_NegBk'),('ILA_Country_ReBk'),('ILA_Country_ConBk'),('ILA_Country_Drawee'),
  ('ILA_PlaceCntry'),('ILA_PortDischCtry'),
  -- ILL (LC Issuance – Local)
  ('LC_Issuance_local_CountryB'),('LC_Issuance_local_CountryABK'),('LC_Issuance_local_CountryD'),
  ('LC_Issuance_local_CountryDrawee'),('LC_Issuance_local_CountryOfOrigin'),('LC_Issuance_local_CountryNB'),
  -- ICR (Import Collection – Receipt)
  ('ICR_DraweeCountry'),('ICR_DrawerCountry'),('ICR_RemCountry'),('ICR_CBCountry'),
  ('ICR_PlaceCntry'),('ICR_PortDischCtry'),
  -- ICA (Import Collection – Acceptance)
  ('ICA_DraweeCountry'),('ICA_DrawerCountry'),('ICA_PlaceCntry'),('ICA_PortDischCtry'),
  -- ICD (Import Collection – Documents)
  ('ICD_DraweeCountry'),('ICD_DrawerCountry'),('ICD_RemCountry'),('ICD_CBCountry'),
  ('ICD_PlaceCntry'),('ICD_PortDischCtry'),('ICD_ThirdDrawerCountry'),
  -- SG (Shipping Guarantee)
  ('SG_AppCountry'),('SG_Address_Cntry1'),('SG_Address_Cntry2'),('SG_Address_Cntry3'),
  ('SG_Country_AppBk'),('SG_Cntry_AdvBk1'),('SG_Cntry_AdvBk2'),('SG_Cntry_AdvBk3'),
  ('SG_Country_NegBk'),('SG_Country_ReBk'),('SG_Country_ConBk'),('SG_Country_Drawee'),
  ('SG_PlaceofReceiptCntry'),('SG_PortDischCtry'),
  -- IOA (Import Outward – Acceptance)
  ('IOA_DraweeCountry'),('IOA_DrawerCountry'),('IOA_PlaceCntry'),('IOA_PortDischCtry'),
  ('IOA_ThirdDrawerCountry'),
  -- IAPP (Import Advance Payment)
  ('IAPP_CountryDrawer'),('IAPP_DraweeCountry'),('IAPP_PlaceCntry'),('IAPP_PortDischCtry'),
  -- ID (Import Documents)
  ('ID_AppCountry'),('ID_Address_Cntry1'),('ID_Country_AppBk'),('ID_Cntry_AdvBk1'),
  ('ID_Cntry_AdvBk2'),('ID_Cntry_AdvBk3'),('ID_Country_NegBk'),('ID_Country_ReBk'),
  ('ILD_Country_ConBk'),('ILD_Country_Drawee'),('ID_PlaceofReceiptCntry'),('ID_PortDischCtry'),
  -- ICL (Import Collection – Local)
  ('icl_CountryA'),('icl_CountryB'),('icl_CountryOfOrigin'),('icl_CountryC'),('icl_CountryD'),
  -- IAP (Import Acceptance / Payment)
  ('IAP_Country'),('IAP_ThirdDrawerCountry'),('IAP_CountryDrawer'),
  -- Multi-value origin / port-of-loading country fields (grid columns) — legacy
  -- Populate_High_Risk_Port_Country, also screened against SANCTIONED_COUNTRIES.
  -- The evaluator aggregates grid rows, so "any row sanctioned" raises the flag.
  ('ILF_CntryOfOrigin'),('ILF_PortLoadingCtry'),
  ('ILA_CountryOfOrigin'),('ILA_PortLoadingCtry'),
  ('ICR_CountryOfOrigin'),('ICR_PortLoadingCtry'),
  ('ICA_CountryOfOrigin'),('ICA_PortLoadingCtry'),
  ('ICD_CountryOfOrigin'),('ICD_PortLoadingCtry'),
  ('SG_CountryOfOrigin'),('SG_PortLoadingCtry'),
  ('IOA_CountryOfOrigin'),('IOA_PortLoadingCtry'),('IOA_cntryoriginGD'),
  ('IAPP_cntryoriginGD'),('IAPP_cntryoforigin'),('IAPP_PortLoadingCtry'),
  ('ID_CountryOfOrigin'),('ID_PortLoadingCtry'),
  ('IAP_CountryOfOrigin');

/* -- 3. Seed the flag ------------------------------------------------------ */
DECLARE @flagId int;
INSERT INTO dbo.TmX_Flag_Catalogue
( Flag_Code, Flag_Name, Flag_Description,
  Flag_Type_Lkp_ID, Severity_Lkp_ID, Default_Weight, Requires_Evidence,
  Active_Flag, Raise_Per_Match, Is_Built_In, Is_Deleted,
  Status, Version_No, Approved_By, Approved_Date,
  Created_By, Created_Date )
VALUES
( 'ARL-SANCTCTRY',
  'Sanctioned country present',
  'Any country field on the transaction — applicant / address / bank / drawee / place / port-of-discharge, plus the multi-value country-of-origin and port-of-loading grid columns, across all trade products — matches the sanctioned-countries list. Generalises the legacy Populate_High_Risk_Countries and Populate_High_Risk_Port_Country screening; raises one flag per matching country field.',
  @autoType, @sev, 1.00, 0,
  1, 1, 0, 0,
  'Active', 1, 'seed', GETDATE(),
  'seed', GETDATE() );
SET @flagId = SCOPE_IDENTITY();

/* Scope: every product that actually owns one of these fields (product-level). */
INSERT INTO dbo.TmX_Flag_Scope
( Flag_ID, Product_ID, Tab_ID, Sort_Order, Active_Flag, Created_By, Created_Date )
SELECT DISTINCT @flagId, fs.Product_Id, NULL, 0, 1, 'seed', GETDATE()
FROM dbo.TmX_Tenant_Field_Setup fs
WHERE fs.Field_Name IN (SELECT code FROM @fields)
  AND fs.Product_Id IS NOT NULL;

-- Fallback: if none resolved (field names differ), scope to Import (product 2).
IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Flag_Scope WHERE Flag_ID = @flagId)
    INSERT INTO dbo.TmX_Flag_Scope (Flag_ID, Product_ID, Tab_ID, Sort_Order, Active_Flag, Created_By, Created_Date)
    VALUES (@flagId, 2, NULL, 0, 1, 'seed', GETDATE());

/* One rule per field, each in its OWN Rule_Group → OR across them. */
INSERT INTO dbo.TmX_Flag_Rule
( Flag_ID, Rule_Group, Evaluation_Order, Logical_Operator,
  Left_Source, Left_Field_Code, Operator,
  Right_Operand_Type, Reference_List_Code,
  Active_Flag, Created_By, Created_Date )
SELECT
  @flagId,
  ROW_NUMBER() OVER (ORDER BY code),   -- distinct group per field → OR
  1, 'AND',
  'Trade', code, 'In',
  'ReferenceList', 'SANCTIONED_COUNTRIES',
  1, 'seed', GETDATE()
FROM @fields;

DECLARE @ruleCount   int = (SELECT COUNT(*) FROM dbo.TmX_Flag_Rule  WHERE Flag_ID = @flagId);
DECLARE @scopeCount  int = (SELECT COUNT(*) FROM dbo.TmX_Flag_Scope WHERE Flag_ID = @flagId);
PRINT CONCAT('Seeded country flag Flag_ID=', @flagId, ' with ', @ruleCount, ' country-field rule(s) across ', @scopeCount, ' product scope(s).');

/* ========================================================================
   4. Second flag — high-risk / sanctioned GOODS (HS code)
   Mirrors the legacy Populate_High_Risk_HSCodes(...) screening: the HS-code
   grid columns across the trade tabs, screened against SCREENING_GOODS
   (→ REPO_SCREENING_GOODS). These are multi-value grid columns; the evaluator
   aggregates rows, so "any HS line is screened" raises the flag. Per-match on.
   ======================================================================== */
DECLARE @hsFields TABLE (code nvarchar(100) PRIMARY KEY);
INSERT INTO @hsFields (code) VALUES
  ('ILF_HScode'),   -- ILF
  ('ILA_HSCode'),   -- ILA
  ('ICR_HSCode'),   -- ICR
  ('ICA_HSCode'),   -- ICA
  ('ICD_HSCode'),   -- ICD
  ('SG_HSCode'),    -- SG
  ('IOA_HSCode'),   -- IOA
  ('IAPP_hs_code'),('IAPP_HSCodeGD'),  -- IAPP (two HS grids)
  ('ID_HSCode'),    -- ID
  ('IAP_HSCode');   -- IAP

DECLARE @hsFlagId int;
INSERT INTO dbo.TmX_Flag_Catalogue
( Flag_Code, Flag_Name, Flag_Description,
  Flag_Type_Lkp_ID, Severity_Lkp_ID, Default_Weight, Requires_Evidence,
  Active_Flag, Raise_Per_Match, Is_Built_In, Is_Deleted,
  Status, Version_No, Approved_By, Approved_Date,
  Created_By, Created_Date )
VALUES
( 'ARL-SCRNGOODS',
  'High-risk goods (HS code)',
  'An HS-code line on the transaction matches the screened-goods list (dual-use / high-risk goods). Generalises the legacy Populate_High_Risk_HSCodes screening across the trade products'' HS-code grids; raises one flag per matching HS line.',
  @autoType, @sev, 1.00, 0,
  1, 1, 0, 0,
  'Active', 1, 'seed', GETDATE(),
  'seed', GETDATE() );
SET @hsFlagId = SCOPE_IDENTITY();

INSERT INTO dbo.TmX_Flag_Scope
( Flag_ID, Product_ID, Tab_ID, Sort_Order, Active_Flag, Created_By, Created_Date )
SELECT DISTINCT @hsFlagId, fs.Product_Id, NULL, 0, 1, 'seed', GETDATE()
FROM dbo.TmX_Tenant_Field_Setup fs
WHERE fs.Field_Name IN (SELECT code FROM @hsFields)
  AND fs.Product_Id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM dbo.TmX_Flag_Scope WHERE Flag_ID = @hsFlagId)
    INSERT INTO dbo.TmX_Flag_Scope (Flag_ID, Product_ID, Tab_ID, Sort_Order, Active_Flag, Created_By, Created_Date)
    VALUES (@hsFlagId, 2, NULL, 0, 1, 'seed', GETDATE());

INSERT INTO dbo.TmX_Flag_Rule
( Flag_ID, Rule_Group, Evaluation_Order, Logical_Operator,
  Left_Source, Left_Field_Code, Operator,
  Right_Operand_Type, Reference_List_Code,
  Active_Flag, Created_By, Created_Date )
SELECT
  @hsFlagId,
  ROW_NUMBER() OVER (ORDER BY code),
  1, 'AND',
  'Trade', code, 'In',
  'ReferenceList', 'SCREENING_GOODS',
  1, 'seed', GETDATE()
FROM @hsFields;

DECLARE @hsRuleCount  int = (SELECT COUNT(*) FROM dbo.TmX_Flag_Rule  WHERE Flag_ID = @hsFlagId);
DECLARE @hsScopeCount int = (SELECT COUNT(*) FROM dbo.TmX_Flag_Scope WHERE Flag_ID = @hsFlagId);
PRINT CONCAT('Seeded goods (HS) flag Flag_ID=', @hsFlagId, ' with ', @hsRuleCount, ' HS-field rule(s) across ', @hsScopeCount, ' product scope(s).');

COMMIT TRANSACTION;
GO

PRINT '2026_06_062_ResetAutoFlagsSeedIlfSanctionedCountry.sql complete.';
GO
