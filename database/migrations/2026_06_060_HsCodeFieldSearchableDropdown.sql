/* ============================================================================
   2026_06_060_HsCodeFieldSearchableDropdown.sql

   Convert HS Code dropdown fields to SearchableDropdown so the dynamic
   FieldRenderer renders a type-to-filter combobox instead of a native
   <select> that mounts the entire ~7,600-row HSCodeList lookup.

   Why: a goods grid with 100+ rows x a native HS-code <select> mounts
   hundreds of thousands of <option> nodes and freezes the page. The fix is
   config-driven (not hardcoded per field): any field set to SearchableDropdown
   (Field_Type_Lkp = 65) renders the combobox, which only ever renders ~50
   matching options.

   Field type codes (dbo.TmX_Lookup, field-type group):
       25 = Dropdown            (Lookup_ID 15)
       65 = SearchableDropdown  (Lookup_ID 2494)

   Targets every Dropdown field bound to the HSCodeList lookup (the HS Code
   column across the trade goods grids — 25 fields across products 2/3/5).

   Idempotent: re-running is a no-op (no Dropdown+HSCodeList rows remain).
   Rollback:
       UPDATE dbo.TmX_Tenant_Field_Setup SET Field_Type_Lkp = 25
       WHERE Field_Type_Lkp = 65 AND Field_Lookup_Type LIKE '%HSCodeList%';

   NOTE: bump the form-schema cache / restart the API after applying so
   GetProductFormDefinition re-reads the new field type.
   ============================================================================ */
SET XACT_ABORT ON;
BEGIN TRAN;

UPDATE dbo.TmX_Tenant_Field_Setup
SET    Field_Type_Lkp = 65                 -- SearchableDropdown
WHERE  Field_Type_Lkp = 25                 -- Dropdown
  AND  Field_Lookup_Type LIKE '%HSCodeList%';

COMMIT;
