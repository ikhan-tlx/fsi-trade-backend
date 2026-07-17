-- =========================================================================
-- 2026_07_025_TradeHsGridReorderDerivedLob.sql
--
-- KYC-LOB Phase 2 (LOB_DERIVATION_DESIGN_AND_PLAN.md), trade (Import=2, Export=3):
--   A. Re-sequence every HS-code grid's columns into the canonical order and
--      rename 'Unit of Measurement' -> label 'UOM' + placeholder 'Unit of Measurement'.
--   B. Add a HIDDEN Derived-LOB dropdown column to each HS grid
--      (Field_Type_Lkp=25 Dropdown, Field_Lookup_Type='LOB', Visibility='0').
--   C. Stamp dbo.TmX_Lookup.Parent_Lookup_ID on each HSCodeList row = the LOB
--      derived from the LOB rows' UDF hs-spec (ranges/eq), ranked
--      most-specific -> highest-risk -> lowest sort.
--
-- Grids are found generically (parent of any 'HS Code' column on products 2/3),
-- so this covers all sub-product grids. Idempotent.
-- Run: sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -I -f 65001 -i "<path>\2026_07_025_TradeHsGridReorderDerivedLob.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

-- grid parents (HS grids on trade products) into a temp table
IF OBJECT_ID('tempdb..#grids') IS NOT NULL DROP TABLE #grids;
SELECT DISTINCT Parent_Tenant_Field_Setup_Id AS pid
INTO #grids
FROM dbo.TmX_Tenant_Field_Setup
WHERE Field_Label = N'HS Code' AND Parent_Tenant_Field_Setup_Id IS NOT NULL AND Product_Id IN (2,3);
DECLARE @gc int = (SELECT COUNT(*) FROM #grids);
PRINT CONCAT('HS grids found: ', @gc);
GO

-- B. Insert hidden Derived-LOB column per grid (copy structural cols from the HS Code row)
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
JOIN #grids g ON g.pid = h.Parent_Tenant_Field_Setup_Id
WHERE h.Field_Label = N'HS Code'
  AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Tenant_Field_Setup x
                  WHERE x.Field_Name = LEFT(h.Field_Name,46) + N'Lob'
                    AND x.Parent_Tenant_Field_Setup_Id = h.Parent_Tenant_Field_Setup_Id);
PRINT CONCAT('Derived-LOB hidden columns inserted: ', @@ROWCOUNT);
GO

-- A. Re-sequence columns per grid (canonical order by label) + rename UOM
;WITH bases AS (
    SELECT c.Parent_Tenant_Field_Setup_Id pid, MIN(c.Field_Sequence) base
    FROM dbo.TmX_Tenant_Field_Setup c JOIN #grids g ON g.pid = c.Parent_Tenant_Field_Setup_Id
    GROUP BY c.Parent_Tenant_Field_Setup_Id)
UPDATE c
   SET c.Field_Sequence = b.base + CASE c.Field_Label
        WHEN N'HS Code' THEN 0
        WHEN N'Goods Description' THEN 1
        WHEN N'Unit of Measurement' THEN 2
        WHEN N'UOM' THEN 2
        WHEN N'HS Code Price' THEN 3
        WHEN N'Quantity' THEN 4
        WHEN N'Unit Price' THEN 5
        WHEN N'Amount' THEN 6
        WHEN N'Country' THEN 7
        WHEN N'HS Code Price (View Only)' THEN 8
        WHEN N'Derived LOB' THEN 9
        ELSE 30 END
FROM dbo.TmX_Tenant_Field_Setup c JOIN bases b ON b.pid = c.Parent_Tenant_Field_Setup_Id;
PRINT CONCAT('Grid columns re-sequenced: ', @@ROWCOUNT);

UPDATE c SET c.Field_Label = N'UOM', c.Placeholder = N'Unit of Measurement'
FROM dbo.TmX_Tenant_Field_Setup c JOIN #grids g ON g.pid = c.Parent_Tenant_Field_Setup_Id
WHERE c.Field_Label = N'Unit of Measurement';
PRINT CONCAT('UOM columns renamed: ', @@ROWCOUNT);
GO

-- C. Stamp HSCodeList.Parent_Lookup_ID from LOB UDF hs-spec (ranked)
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
-- one grid's order + a few stamped HS rows
SELECT TOP 12 c.Field_Name, c.Field_Label, c.Field_Type_Lkp FT, c.Field_Sequence Seq, c.Visibility Vis, c.Placeholder
FROM dbo.TmX_Tenant_Field_Setup c
WHERE c.Parent_Tenant_Field_Setup_Id = (SELECT TOP 1 pid FROM #grids)
ORDER BY c.Field_Sequence;
GO
PRINT '2026_07_025 complete.';
GO
