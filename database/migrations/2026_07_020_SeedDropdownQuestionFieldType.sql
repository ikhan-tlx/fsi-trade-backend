-- =========================================================================
-- 2026_07_020_SeedDropdownQuestionFieldType.sql
--
-- 2026-07-13 — ESI — part 2 of 4.
-- Introduces a new field-control type "DropdownQuestion" — a questionnaire
-- row (question text + single-select dropdown answer). Like checkbox_file,
-- the TYPE decides its own rendering (one question per row), so any future
-- questionnaire is metadata-driven.
--
-- Field types live in the FIELD lookup group (Lookup_Type = 'FIELD'):
--   Visible_Value = type NAME, Hidden_Value = the Field_Type_Lkp code.
-- (TenantFieldSetup.Field_Type_Lkp stores that Hidden_Value int — see
--  AutoFlagReadQueries / FieldTypeMapper.)
--
-- New code: 66 (next free after 65 = SearchableDropdown). Must be mirrored in
--   BE  FieldTypeMapper.ToSemanticType  (66 => "dropdownQuestion")
--   BE  AutoFlagReadQueries             (allowed set + ftype = "Dropdown")
--   FE  compileSchema.mapTypeToken      ("dropdownquestion" -> render kind)
--   FE  FieldRenderer                   (question-row control)
--
-- Idempotent: guarded by NOT EXISTS on (Lookup_Type='FIELD', Visible_Value).
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

DECLARE @now    datetime      = GETUTCDATE();
DECLARE @nowStr nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who    nvarchar(100) = N'System (DropdownQuestion field-type seed)';

-- Tenant/Locale from an existing FIELD row so the new type sits in the same scope.
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Lookup WHERE Lookup_Type = N'FIELD' ORDER BY Lookup_ID);
DECLARE @localeId int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Lookup WHERE Lookup_Type = N'FIELD' ORDER BY Lookup_ID);
IF @tenantId IS NULL SET @tenantId = 1;
IF @localeId IS NULL SET @localeId = 1;

-- Guard: 66 must not already be taken by another FIELD type.
IF EXISTS (SELECT 1 FROM dbo.TmX_Lookup WHERE Lookup_Type = N'FIELD' AND Hidden_Value = N'66'
           AND Visible_Value <> N'DropdownQuestion')
BEGIN
    PRINT 'WARNING: FIELD code 66 already used by a different type — choose another code and update the BE/FE mappers.';
END

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID, Lookup_Type, Visible_Value,       Hidden_Value, Description,
     Is_Active, Active_Flag, User_Editable, Sort_Order, Lookup_Name,
     Locale_ID, Locale_Label,           Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT
     @tenantId, N'FIELD',     N'DropdownQuestion', N'66',        N'Questionnaire row: question text + single-select dropdown answer.',
     1,         1,           0,             900,        N'DropdownQuestion',
     @localeId, N'DropdownQuestion',     @who,       @nowStr,      @who,            @now
WHERE NOT EXISTS (
     SELECT 1 FROM dbo.TmX_Lookup l
     WHERE l.Lookup_Type = N'FIELD' AND l.Visible_Value = N'DropdownQuestion');

PRINT CONCAT('DropdownQuestion field-type: inserted ', @@ROWCOUNT, ' row(s).');

SELECT Lookup_ID, Lookup_Type, Visible_Value, Hidden_Value
FROM   dbo.TmX_Lookup WHERE Lookup_Type = N'FIELD' AND Visible_Value = N'DropdownQuestion';
GO

PRINT 'DropdownQuestion field-type seed complete.';
