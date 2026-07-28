-- =========================================================================
-- 2026_07_035_TradeHsGridReorderDerivedLob_SafeRebuild.sql
--
-- Replaces the WITHDRAWN 2026_07_025_TradeHsGridReorderDerivedLob.sql, which
-- scrambled the LC Issuance "General Info" tab (fields out of order; the
-- "Country" fields collapsed into a repeated "Country, Country, Country…" run).
--
-- KYC-LOB Phase 2 (LOB_DERIVATION_DESIGN_AND_PLAN.md), trade (Import=2, Export=3):
--   A. Re-sequence each HS-code GRID's OWN columns into the canonical order and
--      rename 'Unit of Measurement' -> label 'UOM' + placeholder 'Unit of Measurement'.
--   B. Add a HIDDEN Derived-LOB dropdown column to each HS grid
--      (Field_Type_Lkp=25 Dropdown, Field_Lookup_Type='LOB', Visibility='0').
--   C. Stamp dbo.TmX_Lookup.Parent_Lookup_ID on each HSCodeList row from the
--      LOB rows' UDF hs-spec.
--
-- -------------------------------------------------------------------------
-- WHAT WENT WRONG IN 025 (and how this fixes it)
--   025 Part A had two defects:
--     1. It picked grids as "the PARENT of any 'HS Code' column" then
--        re-sequenced EVERY child of that parent. On LC Issuance that parent is
--        shared with the inline General-Info fields, so those were re-sequenced
--        too — fields it must never touch.
--     2. It set Field_Sequence = base + CASE(Field_Label) on the NON-UNIQUE
--        label with NO tie-breaker, so every 'Country' field collapsed onto one
--        sequence value and every non-whitelisted field (incl. section-header
--        rows) onto another → identical sequences → undefined render order.
--   This rebuild:
--     * keys the grid set on (parent, Field_Table_Name) from the HS-Code column
--       and only re-sequences rows whose Field_Table_Name matches that HS-grid
--       table — the grid's OWN columns. Inline section fields (a different
--       Field_Table_Name, e.g. TmxTransactionDetail[]) are never touched.
--     * re-sequences with ROW_NUMBER() (canonical rank, then existing sequence,
--       then Field_Name) so every column gets a DISTINCT sequence — collisions
--       are impossible.
--
--   DAMAGED ENVIRONMENTS: this migration only re-sequences GRID columns. It does
--   NOT restore the inline General-Info fields that the old 025 clobbered —
--   restore those Field_Sequence values from your TmX_Tenant_Field_Setup backup
--   FIRST, then run this.
--
-- Idempotent. Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -I -f 65001 -i "<path>\2026_07_035_TradeHsGridReorderDerivedLob_SafeRebuild.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- Grid identity = (parent, grid table) of each 'HS Code' column on trade products.
-- Carrying Field_Table_Name confines every step to the grid's own columns and
-- excludes the inline General-Info fields that share the parent.
IF OBJECT_ID('tempdb..#grids') IS NOT NULL DROP TABLE #grids;
SELECT DISTINCT
       Parent_Tenant_Field_Setup_Id AS pid,
       Field_Table_Name             AS tbl
INTO   #grids
FROM   dbo.TmX_Tenant_Field_Setup
WHERE  Field_Label = N'HS Code'
  AND  Parent_Tenant_Field_Setup_Id IS NOT NULL
  AND  Field_Table_Name IS NOT NULL
  AND  Product_Id IN (2, 3);
PRINT CONCAT('HS grids found: ', (SELECT COUNT(*) FROM #grids));
GO

-- B. Insert hidden Derived-LOB column per grid (structural cols copied from the HS Code row)
INSERT INTO dbo.TmX_Tenant_Field_Setup
    (Tenant_Id, Product_Id, Tab_Id, Parent_Tenant_Field_Setup_Id, Field_Name, Field_Label,
     Field_Type_Lkp, Field_Sequence, Field_Table_Name, Field_Lookup_Type,
     Is_Mandatory, Is_Disabled, Visibility, Is_Active, Placeholder,
     Created_By, Created_Date, Locale_ID, Locale_Field_Label)
SELECT h.Tenant_Id, h.Product_Id, h.Tab_Id, h.Parent_Tenant_Field_Setup_Id,
       LEFT(h.Field_Name,46) + N'Lob', N'Derived LOB',
       25, h.Field_Sequence + 200, h.Field_Table_Name, N'LOB',
       N'0', N'0', N'0', 1, N'Derived Line of Business (auto)',
       N'System (LOB derive)', GETUTCDATE(), h.Locale_ID, N'Derived LOB'
FROM dbo.TmX_Tenant_Field_Setup h
JOIN #grids g ON g.pid = h.Parent_Tenant_Field_Setup_Id AND g.tbl = h.Field_Table_Name
WHERE h.Field_Label = N'HS Code'
  AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup x
                  WHERE x.Field_Name = LEFT(h.Field_Name,46) + N'Lob'
                    AND x.Parent_Tenant_Field_Setup_Id = h.Parent_Tenant_Field_Setup_Id);
PRINT CONCAT('Derived-LOB hidden columns inserted: ', @@ROWCOUNT);
GO

-- A. Re-sequence GRID columns only (canonical order, collision-free via ROW_NUMBER).
;WITH ranked AS (
    SELECT c.Tenant_Field_Setup_Id AS id,
           MIN(c.Field_Sequence) OVER (PARTITION BY c.Parent_Tenant_Field_Setup_Id, c.Field_Table_Name)
           + ROW_NUMBER() OVER (
                 PARTITION BY c.Parent_Tenant_Field_Setup_Id, c.Field_Table_Name
                 ORDER BY CASE c.Field_Label
                            WHEN N'HS Code'                  THEN 0
                            WHEN N'Goods Description'         THEN 1
                            WHEN N'Unit of Measurement'       THEN 2
                            WHEN N'UOM'                       THEN 2
                            WHEN N'HS Code Price'             THEN 3
                            WHEN N'Quantity'                  THEN 4
                            WHEN N'Unit Price'                THEN 5
                            WHEN N'Amount'                    THEN 6
                            WHEN N'Country'                   THEN 7
                            WHEN N'HS Code Price (View Only)' THEN 8
                            WHEN N'Derived LOB'               THEN 9
                            ELSE 30 END,
                          c.Field_Sequence,      -- stable tie-breaker
                          c.Field_Name)          -- final tie-breaker (always unique)
           - 1 AS newseq
    FROM dbo.TmX_Tenant_Field_Setup c
    JOIN #grids g ON g.pid = c.Parent_Tenant_Field_Setup_Id
                 AND g.tbl = c.Field_Table_Name
)
UPDATE t
   SET t.Field_Sequence = r.newseq
FROM dbo.TmX_Tenant_Field_Setup t
JOIN ranked r ON r.id = t.Tenant_Field_Setup_Id;
PRINT CONCAT('Grid columns re-sequenced: ', @@ROWCOUNT);

-- Rename UOM (grid columns only)
UPDATE c SET c.Field_Label = N'UOM', c.Placeholder = N'Unit of Measurement'
FROM dbo.TmX_Tenant_Field_Setup c
JOIN #grids g ON g.pid = c.Parent_Tenant_Field_Setup_Id AND g.tbl = c.Field_Table_Name
WHERE c.Field_Label = N'Unit of Measurement';
PRINT CONCAT('UOM columns renamed: ', @@ROWCOUNT);
GO

-- C. Stamp HSCodeList.Parent_Lookup_ID from LOB UDF hs-spec (ranked). Unchanged from original.
;WITH spec AS (
    SELECT lob.Lookup_ID lobId,
           CASE WHEN JSON_VALUE(lob.UDF,'$.risk') LIKE N'High (Auto)%' THEN 3
                WHEN JSON_VALUE(lob.UDF,'$.risk') LIKE N'High%'        THEN 2
                ELSE 1 END AS riskRank,
           lob.Sort_Order srt,
           COALESCE(j.[from], j.eq) f, COALESCE(j.[to], j.eq) t,
           LEN(COALESCE(j.[from], j.eq)) d
    FROM dbo.TmX_Lookup lob
    CROSS APPLY OPENJSON(lob.UDF, '$.hs')
        WITH ([from] nvarchar(10) '$.from', [to] nvarchar(10) '$.to', eq nvarchar(10) '$.eq') j
    WHERE lob.Lookup_Type = N'LOB' AND COALESCE(j.[from], j.eq) IS NOT NULL
),
matched AS (
    SELECT h.Lookup_ID hsId, s.lobId,
           ROW_NUMBER() OVER (PARTITION BY h.Lookup_ID ORDER BY s.d DESC, s.riskRank DESC, s.srt ASC) rn
    FROM dbo.TmX_Lookup h
    JOIN spec s ON h.Lookup_Type = N'HSCodeList'
       AND LEFT(REPLACE(REPLACE(REPLACE(h.Hidden_Value,'.',''),' ',''),'-',''), s.d) BETWEEN s.f AND s.t
)
UPDATE h SET Parent_Lookup_ID = mt.lobId
FROM dbo.TmX_Lookup h JOIN matched mt ON mt.hsId = h.Lookup_ID AND mt.rn = 1;
PRINT CONCAT('HSCodeList rows stamped with LOB parent: ', @@ROWCOUNT);
GO

-- sanity
SELECT (SELECT COUNT(*) FROM #grids) AS HS_Grids,
       (SELECT COUNT(*) FROM dbo.TmX_Tenant_Field_Setup WHERE Field_Label=N'Derived LOB' AND Field_Lookup_Type=N'LOB') AS DerivedLobFields,
       (SELECT COUNT(*) FROM dbo.TmX_Lookup WHERE Lookup_Type=N'HSCodeList' AND Parent_Lookup_ID IS NOT NULL) AS HsStamped,
       (SELECT COUNT(*) FROM dbo.TmX_Lookup WHERE Lookup_Type=N'HSCodeList') AS HsTotal;
SELECT TOP 12 c.Field_Name, c.Field_Label, c.Field_Type_Lkp FT, c.Field_Sequence Seq, c.Visibility Vis, c.Placeholder
FROM dbo.TmX_Tenant_Field_Setup c
WHERE c.Parent_Tenant_Field_Setup_Id = (SELECT TOP 1 pid FROM #grids)
  AND c.Field_Table_Name = (SELECT TOP 1 tbl FROM #grids)
ORDER BY c.Field_Sequence;
GO
PRINT '2026_07_035 complete (safe rebuild of 025).';
GO
