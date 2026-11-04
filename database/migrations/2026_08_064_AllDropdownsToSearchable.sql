/* ============================================================================
   2026_08_064_AllDropdownsToSearchable.sql

   Convert EVERY dynamic-field Dropdown to Searchable Dropdown so the dynamic
   FieldRenderer renders a type-to-filter combobox instead of a native <select>.

   Scope: ALL Dropdown fields (Field_Type_Lkp = 25) in dbo.TmX_Tenant_Field_Setup,
   across every product / tab / section, INCLUDING table columns. The
   Field_Lookup_Type binding is kept unchanged — only the input control changes
   (plain select -> type-to-filter). Multi-select (26) and dropdownQuestion (66)
   are NOT touched; only single-select Dropdown (25) -> SearchableDropdown (65).

   Field type codes (dbo.TmX_Lookup, FIELD group):
       25 = Dropdown
       65 = SearchableDropdown

   Idempotent: rows already at 65 are untouched; safe to re-run (a re-run converts 0).
   Rollback (reverts ALL searchable dropdowns back to plain dropdowns):
       UPDATE dbo.TmX_Tenant_Field_Setup SET Field_Type_Lkp = 25 WHERE Field_Type_Lkp = 65;

   NOTE: bump the form-schema cache / restart the API after applying so
   GetProductFormDefinition re-reads the new field type (FieldTypeMapper maps
   65 -> searchableSelect at read time; the FE compileSchema/FieldRenderer render it).
   Run:
     sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_064_AllDropdownsToSearchable.sql"
   ============================================================================ */
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @before INT = (SELECT COUNT(*) FROM dbo.TmX_Tenant_Field_Setup WHERE Field_Type_Lkp = 25);

BEGIN TRAN;

UPDATE dbo.TmX_Tenant_Field_Setup
SET    Field_Type_Lkp = 65                 -- SearchableDropdown
WHERE  Field_Type_Lkp = 25;                -- Dropdown (all bindings)

DECLARE @changed INT = @@ROWCOUNT;

COMMIT;

DECLARE @remaining INT = (SELECT COUNT(*) FROM dbo.TmX_Tenant_Field_Setup WHERE Field_Type_Lkp = 25);
DECLARE @searchable INT = (SELECT COUNT(*) FROM dbo.TmX_Tenant_Field_Setup WHERE Field_Type_Lkp = 65);

PRINT CONCAT('2026_08_064 complete - converted ', @changed, ' Dropdown field(s) 25 -> 65 (Searchable). ',
             'Dropdowns before=', @before, ', remaining=', @remaining, ', total Searchable now=', @searchable, '.');
GO
