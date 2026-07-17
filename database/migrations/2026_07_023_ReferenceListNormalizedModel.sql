/* ============================================================================
   2026_07_023_ReferenceListNormalizedModel.sql

   Generic, normalized Reference-List model (design: REFERENCE_LIST_MODULE_
   DESIGN_AND_PLAN.md). Introduces three additive tables and migrates the two
   legacy REPO_* watchlists plus the custom TmX_Reference_List(_Value) rows into
   them. Nothing legacy is dropped — old tables stay live until the parity gate
   (plan §7 step 6), so this migration is safe to run on a live system.

     • TmX_Reference_Type            — the CATEGORY a rule points at
                                       (Type_Code == today's Reference_List_Code).
     • TmX_Reference_Value           — the DEDUPED value (a country, an HS code…).
                                       Code == TmX_Lookup.Hidden_Value (D1-a).
     • TmX_Reference_Value_Type_Map  — the M:N membership (a value in many types).

   Decisions baked in (plan §8):
     D1-a  match on Code only; ISO3 / synonyms / risk go to Udf_Data.
     D5    V_DATA_SOURCE is DECOMPOSED into 5 atomic country attribute types
           (HIGH_RISK / GREY_LISTED / BLACK_LISTED / LANDLOCKED / TAX_HAVEN),
           one Map row per attribute, plus a SANCTIONED_COUNTRIES umbrella that
           holds every migrated country (backward-compat for existing rules).

   Lookup_Type is discovered from TmX_Tenant_Field_Setup (the country fields'
   Field_Lookup_Type) so the FE domain filter + value picker line up with the
   real lookup group. Matching does NOT depend on it (matching is by Code).

   Idempotent + transactional. Re-running tops up any missing rows without
   duplicating (all inserts are NOT EXISTS / left-join guarded).

   Run:
     sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_07_023_ReferenceListNormalizedModel.sql"
   ============================================================================ */

USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- sqlcmd defaults QUOTED_IDENTIFIER OFF; filtered indexes + FOR JSON need it ON.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ─────────────────────────────────────────────────────────────────────────
   1. TABLES (additive, idempotent)
   ───────────────────────────────────────────────────────────────────────── */
/* Tables are created without their indexes; indexes follow as individually
   guarded statements so a partially-created table (e.g. from an aborted run)
   is repaired on re-run rather than skipped. */
IF OBJECT_ID('dbo.TmX_Reference_Type', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Reference_Type
    (
        Reference_Type_Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Reference_Type PRIMARY KEY,
        Type_Code     NVARCHAR(80)  NOT NULL,
        Type_Name     NVARCHAR(200) NOT NULL,
        Description   NVARCHAR(1000) NULL,
        Lookup_Type   NVARCHAR(100) NULL,           -- the TmX_Lookup group (domain)
        Udf_Schema    NVARCHAR(MAX) NULL,           -- JSON: [{key,label,type,...}]
        Active_Flag   BIT           NOT NULL CONSTRAINT DF_TmX_RefType_Active DEFAULT (1),
        Source        NVARCHAR(20)  NOT NULL CONSTRAINT DF_TmX_RefType_Source DEFAULT ('user'),
        Created_By    NVARCHAR(100) NULL,
        Created_Date  DATETIME2(7)  NOT NULL CONSTRAINT DF_TmX_RefType_Created DEFAULT (SYSUTCDATETIME()),
        Last_Updated_By   NVARCHAR(100) NULL,
        Last_Updated_Date DATETIME2(7)  NULL
    );
    PRINT 'Created dbo.TmX_Reference_Type.';
END
ELSE PRINT 'dbo.TmX_Reference_Type already exists — skipped.';
GO

IF OBJECT_ID('dbo.TmX_Reference_Value', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Reference_Value
    (
        Reference_Value_Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Reference_Value PRIMARY KEY,
        Code          NVARCHAR(100) NOT NULL,       -- == TmX_Lookup.Hidden_Value
        Name          NVARCHAR(400) NULL,
        Description   NVARCHAR(1000) NULL,
        Lookup_Type   NVARCHAR(100) NULL,           -- domain group (NULL = literal)
        Udf_Data      NVARCHAR(MAX) NULL,           -- JSON: {key:value}
        Active_Flag   BIT           NOT NULL CONSTRAINT DF_TmX_RefVal_Active DEFAULT (1),
        Created_By    NVARCHAR(100) NULL,
        Created_Date  DATETIME2(7)  NOT NULL CONSTRAINT DF_TmX_RefVal_Created DEFAULT (SYSUTCDATETIME()),
        Last_Updated_By   NVARCHAR(100) NULL,
        Last_Updated_Date DATETIME2(7)  NULL
    );
    PRINT 'Created dbo.TmX_Reference_Value.';
END
ELSE PRINT 'dbo.TmX_Reference_Value already exists — skipped.';
GO

IF OBJECT_ID('dbo.TmX_Reference_Value_Type_Map', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Reference_Value_Type_Map
    (
        Map_Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_Reference_Value_Type_Map PRIMARY KEY,
        Reference_Value_Id BIGINT NOT NULL
            CONSTRAINT FK_TmX_RVTM_Value REFERENCES dbo.TmX_Reference_Value (Reference_Value_Id),
        Reference_Type_Id  BIGINT NOT NULL
            CONSTRAINT FK_TmX_RVTM_Type  REFERENCES dbo.TmX_Reference_Type (Reference_Type_Id),
        Active_Flag   BIT           NOT NULL CONSTRAINT DF_TmX_RVTM_Active DEFAULT (1),
        Created_By    NVARCHAR(100) NULL,
        Created_Date  DATETIME2(7)  NOT NULL CONSTRAINT DF_TmX_RVTM_Created DEFAULT (SYSUTCDATETIME())
    );
    PRINT 'Created dbo.TmX_Reference_Value_Type_Map.';
END
ELSE PRINT 'dbo.TmX_Reference_Value_Type_Map already exists — skipped.';
GO

/* Indexes — each guarded independently (idempotent + repairs partial runs). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_TmX_Reference_Type_Code' AND object_id=OBJECT_ID('dbo.TmX_Reference_Type'))
    CREATE UNIQUE INDEX UX_TmX_Reference_Type_Code ON dbo.TmX_Reference_Type (Type_Code);
GO
/* group-scoped uniqueness: Hidden_Value is unique only within a Lookup_Type.
   A NULL Lookup_Type (literal domain) is treated as its own group. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_TmX_Reference_Value_LkpCode' AND object_id=OBJECT_ID('dbo.TmX_Reference_Value'))
    CREATE UNIQUE INDEX UX_TmX_Reference_Value_LkpCode ON dbo.TmX_Reference_Value (Lookup_Type, Code) WHERE Lookup_Type IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_TmX_Reference_Value_Code_NoLkp' AND object_id=OBJECT_ID('dbo.TmX_Reference_Value'))
    CREATE UNIQUE INDEX UX_TmX_Reference_Value_Code_NoLkp ON dbo.TmX_Reference_Value (Code) WHERE Lookup_Type IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_TmX_RVTM_Value_Type' AND object_id=OBJECT_ID('dbo.TmX_Reference_Value_Type_Map'))
    CREATE UNIQUE INDEX UX_TmX_RVTM_Value_Type ON dbo.TmX_Reference_Value_Type_Map (Reference_Value_Id, Reference_Type_Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TmX_RVTM_Type_Active' AND object_id=OBJECT_ID('dbo.TmX_Reference_Value_Type_Map'))
    CREATE INDEX IX_TmX_RVTM_Type_Active ON dbo.TmX_Reference_Value_Type_Map (Reference_Type_Id, Active_Flag);
GO

/* Widen Name to hold REPO_SCREENING_GOODS.V_GOOD_NAME (NVARCHAR(3999)).
   Idempotent — only alters when narrower than 4000 chars (8000 bytes). */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.TmX_Reference_Value')
             AND name = 'Name' AND max_length >= 0 AND max_length < 8000)
    ALTER TABLE dbo.TmX_Reference_Value ALTER COLUMN Name NVARCHAR(4000) NULL;
GO

/* ─────────────────────────────────────────────────────────────────────────
   2. DISCOVER the domain (Lookup_Type) for countries + goods
   ───────────────────────────────────────────────────────────────────────── */
BEGIN TRANSACTION;

DECLARE @countryLkp NVARCHAR(100) =
    (SELECT TOP 1 fs.Field_Lookup_Type
       FROM dbo.TmX_Tenant_Field_Setup fs
      WHERE fs.Field_Lookup_Type IS NOT NULL
        AND fs.Field_Name IN ('ILF_AppCountry','ILF_CntryOfOrigin','ILF_PortLoadingCtry',
                               'ILA_AppCountry','ICR_DraweeCountry','SG_AppCountry')
      ORDER BY fs.Field_Lookup_Type);

DECLARE @goodsLkp NVARCHAR(100) =
    (SELECT TOP 1 fs.Field_Lookup_Type
       FROM dbo.TmX_Tenant_Field_Setup fs
      WHERE fs.Field_Lookup_Type IS NOT NULL
        AND fs.Field_Name IN ('ILF_HScode','ILA_HSCode','ICR_HSCode','SG_HSCode','ID_HSCode'));

PRINT CONCAT('Discovered country Lookup_Type = ', ISNULL(@countryLkp,'(none)'),
             '; goods Lookup_Type = ', ISNULL(@goodsLkp,'(literal / none)'));

/* ─────────────────────────────────────────────────────────────────────────
   3. SEED TYPES  (idempotent by Type_Code)
   ───────────────────────────────────────────────────────────────────────── */
DECLARE @country_schema NVARCHAR(MAX) =
  N'[{"key":"iso3","label":"ISO3 code","type":"string","maxLength":3},{"key":"synonyms","label":"Synonyms","type":"string","maxLength":200},{"key":"riskScore","label":"Risk score","type":"number","min":0,"max":100}]';
DECLARE @goods_schema NVARCHAR(MAX) =
  N'[{"key":"description","label":"Description","type":"string","maxLength":500}]';

/* ASCII-only descriptions (no smart punctuation) so the values are codepage-safe
   regardless of how sqlcmd reads this file. Seeded into a table variable so we can
   both INSERT missing rows AND heal existing ones (refresh name/description) on
   re-run — this corrects any rows written by an earlier run under a non-UTF8 codepage. */
DECLARE @seed TABLE (Type_Code NVARCHAR(80), Type_Name NVARCHAR(200), Description NVARCHAR(1000),
                     Lookup_Type NVARCHAR(100), Udf_Schema NVARCHAR(MAX), Source NVARCHAR(20));
INSERT INTO @seed (Type_Code, Type_Name, Description, Lookup_Type, Udf_Schema, Source) VALUES
  ('HIGH_RISK_COUNTRY',   'High-risk country',   'Jurisdictions rated high risk by the country-risk model.',        @countryLkp, @country_schema, 'migrated'),
  ('GREY_LISTED_COUNTRY', 'Grey-listed country', 'FATF increased-monitoring (grey list) jurisdictions.',           @countryLkp, @country_schema, 'migrated'),
  ('BLACK_LISTED_COUNTRY','Black-listed country','FATF call-for-action (black list) jurisdictions.',               @countryLkp, @country_schema, 'migrated'),
  ('LANDLOCKED_COUNTRY',  'Landlocked country',  'No sea border - third-country port routing is expected.',        @countryLkp, @country_schema, 'migrated'),
  ('TAX_HAVEN_COUNTRY',   'Tax-haven country',   'Offshore financial centres used by counterparty-routing rules.', @countryLkp, @country_schema, 'migrated'),
  ('SANCTIONED_COUNTRIES','Sanctioned countries','Umbrella category - every migrated sanctioned / high-risk country (backward-compat).', @countryLkp, @country_schema, 'migrated'),
  ('SCREENING_GOODS',     'Screened goods',      'Controlled-goods HS codes screened on every transaction.',       @goodsLkp,   @goods_schema,   'migrated');

INSERT INTO dbo.TmX_Reference_Type (Type_Code, Type_Name, Description, Lookup_Type, Udf_Schema, Active_Flag, Source, Created_By)
SELECT s.Type_Code, s.Type_Name, s.Description, s.Lookup_Type, s.Udf_Schema, 1, s.Source, 'migration'
FROM @seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Reference_Type t WHERE t.Type_Code = s.Type_Code);

/* Heal existing built-in rows: refresh the display name + description (safe — these
   are system-seeded categories), and backfill Lookup_Type/schema if missing. */
UPDATE t
   SET t.Type_Name   = s.Type_Name,
       t.Description  = s.Description,
       t.Lookup_Type  = COALESCE(t.Lookup_Type, s.Lookup_Type),
       t.Udf_Schema   = CASE WHEN NULLIF(t.Udf_Schema,'') IS NULL OR t.Udf_Schema = '[]' THEN s.Udf_Schema ELSE t.Udf_Schema END
FROM dbo.TmX_Reference_Type t
JOIN @seed s ON s.Type_Code = t.Type_Code;

/* Migrate the custom TmX_Reference_List rows → Types (Type_Code == List_Code). */
IF OBJECT_ID('dbo.TmX_Reference_List','U') IS NOT NULL
    INSERT INTO dbo.TmX_Reference_Type (Type_Code, Type_Name, Description, Lookup_Type, Udf_Schema, Active_Flag, Source, Created_By)
    SELECT rl.List_Code, rl.List_Name, 'Migrated from TmX_Reference_List.', NULL, N'[]', rl.Active_Flag, 'migrated', 'migration'
    FROM dbo.TmX_Reference_List rl
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Reference_Type t WHERE t.Type_Code = rl.List_Code);

DECLARE @cTypes int = (SELECT COUNT(*) FROM dbo.TmX_Reference_Type);
PRINT CONCAT('Reference types after seed: ', @cTypes);

/* ─────────────────────────────────────────────────────────────────────────
   4. BACKFILL VALUES  (deduped by Lookup_Type + Code)
   ───────────────────────────────────────────────────────────────────────── */
/* 4a. Countries from REPO_SANCTIONED_COUNTRIES */
IF OBJECT_ID('dbo.REPO_SANCTIONED_COUNTRIES','U') IS NOT NULL
BEGIN
    ;WITH src AS (
        SELECT
            LTRIM(RTRIM(c.V_ISO_COUNTRY_CODE)) AS Code,
            MAX(c.V_COUNTRY_NAME)              AS Name,
            MAX(c.V_ISO3_COUNTRY_CODE)         AS Iso3,
            MAX(c.V_COUNTRY_SYNONYMS)          AS Synonyms,
            MAX(c.N_RISK_SCORE)                AS RiskScore
        FROM dbo.REPO_SANCTIONED_COUNTRIES c
        WHERE NULLIF(LTRIM(RTRIM(c.V_ISO_COUNTRY_CODE)), '') IS NOT NULL
        GROUP BY LTRIM(RTRIM(c.V_ISO_COUNTRY_CODE))
    )
    INSERT INTO dbo.TmX_Reference_Value (Code, Name, Lookup_Type, Udf_Data, Active_Flag, Created_By)
    SELECT s.Code, s.Name, @countryLkp,
           (SELECT s.Iso3 AS iso3, s.Synonyms AS synonyms, s.RiskScore AS riskScore
              FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           1, 'migration'
    FROM src s
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Reference_Value v
        WHERE v.Code = s.Code
          AND ((v.Lookup_Type = @countryLkp) OR (v.Lookup_Type IS NULL AND @countryLkp IS NULL)));
END

/* 4b. Goods from REPO_SCREENING_GOODS */
IF OBJECT_ID('dbo.REPO_SCREENING_GOODS','U') IS NOT NULL
BEGIN
    ;WITH src AS (
        SELECT LTRIM(RTRIM(g.V_GOOD_CODE)) AS Code,
               MAX(g.V_GOOD_NAME)          AS Name,
               MAX(g.V_GOOD_DESCRIPTION)   AS Descr
        FROM dbo.REPO_SCREENING_GOODS g
        WHERE NULLIF(LTRIM(RTRIM(g.V_GOOD_CODE)), '') IS NOT NULL
        GROUP BY LTRIM(RTRIM(g.V_GOOD_CODE))
    )
    INSERT INTO dbo.TmX_Reference_Value (Code, Name, Lookup_Type, Udf_Data, Active_Flag, Created_By)
    SELECT s.Code, s.Name, @goodsLkp,
           (SELECT s.Descr AS [description] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           1, 'migration'
    FROM src s
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Reference_Value v
        WHERE v.Code = s.Code
          AND ((v.Lookup_Type = @goodsLkp) OR (v.Lookup_Type IS NULL AND @goodsLkp IS NULL)));
END

/* 4c. Custom list values from TmX_Reference_List_Value (literal domain). */
IF OBJECT_ID('dbo.TmX_Reference_List_Value','U') IS NOT NULL
    INSERT INTO dbo.TmX_Reference_Value (Code, Name, Lookup_Type, Udf_Data, Active_Flag, Created_By)
    SELECT DISTINCT LTRIM(RTRIM(rv.Value)), LTRIM(RTRIM(rv.Value)), NULL, NULL, 1, 'migration'
    FROM dbo.TmX_Reference_List_Value rv
    WHERE NULLIF(LTRIM(RTRIM(rv.Value)), '') IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TmX_Reference_Value v
          WHERE v.Code = LTRIM(RTRIM(rv.Value)) AND v.Lookup_Type IS NULL);

DECLARE @cValues int = (SELECT COUNT(*) FROM dbo.TmX_Reference_Value);
PRINT CONCAT('Reference values after backfill: ', @cValues);

/* ─────────────────────────────────────────────────────────────────────────
   5. MAP (M:N)
   ───────────────────────────────────────────────────────────────────────── */
DECLARE @tSanct  BIGINT = (SELECT Reference_Type_Id FROM dbo.TmX_Reference_Type WHERE Type_Code='SANCTIONED_COUNTRIES');
DECLARE @tGoods  BIGINT = (SELECT Reference_Type_Id FROM dbo.TmX_Reference_Type WHERE Type_Code='SCREENING_GOODS');

/* 5a. Every migrated country → SANCTIONED_COUNTRIES umbrella. */
IF OBJECT_ID('dbo.REPO_SANCTIONED_COUNTRIES','U') IS NOT NULL AND @tSanct IS NOT NULL
    INSERT INTO dbo.TmX_Reference_Value_Type_Map (Reference_Value_Id, Reference_Type_Id, Active_Flag, Created_By)
    SELECT DISTINCT v.Reference_Value_Id, @tSanct, 1, 'migration'
    FROM dbo.TmX_Reference_Value v
    WHERE ((v.Lookup_Type = @countryLkp) OR (v.Lookup_Type IS NULL AND @countryLkp IS NULL))
      AND EXISTS (SELECT 1 FROM dbo.REPO_SANCTIONED_COUNTRIES c WHERE LTRIM(RTRIM(c.V_ISO_COUNTRY_CODE)) = v.Code)
      AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Reference_Value_Type_Map m
                      WHERE m.Reference_Value_Id = v.Reference_Value_Id AND m.Reference_Type_Id = @tSanct);

/* 5b. Decompose V_DATA_SOURCE → atomic attribute types (D5).
      Split on '/', strip a trailing 'Country' word, trim, map to a Type_Code. */
IF OBJECT_ID('dbo.REPO_SANCTIONED_COUNTRIES','U') IS NOT NULL
BEGIN
    ;WITH tokens AS (
        SELECT LTRIM(RTRIM(c.V_ISO_COUNTRY_CODE)) AS Code,
               LTRIM(RTRIM(
                 CASE WHEN RIGHT(LTRIM(RTRIM(s.value)), 8) = ' Country'
                      THEN LEFT(LTRIM(RTRIM(s.value)), LEN(LTRIM(RTRIM(s.value))) - 8)
                      WHEN LTRIM(RTRIM(s.value)) = 'Country' THEN ''
                      ELSE LTRIM(RTRIM(s.value)) END)) AS Attr
        FROM dbo.REPO_SANCTIONED_COUNTRIES c
        CROSS APPLY STRING_SPLIT(ISNULL(c.V_DATA_SOURCE, ''), '/') s
        WHERE NULLIF(LTRIM(RTRIM(c.V_ISO_COUNTRY_CODE)), '') IS NOT NULL
    ),
    mapped AS (
        SELECT t.Code,
               CASE t.Attr
                   WHEN 'High Risk'   THEN 'HIGH_RISK_COUNTRY'
                   WHEN 'Grey Listed' THEN 'GREY_LISTED_COUNTRY'
                   WHEN 'Black Listed'THEN 'BLACK_LISTED_COUNTRY'
                   WHEN 'LandLocked'  THEN 'LANDLOCKED_COUNTRY'
                   WHEN 'Land Locked' THEN 'LANDLOCKED_COUNTRY'
                   WHEN 'Tax Haven'   THEN 'TAX_HAVEN_COUNTRY'
                   ELSE NULL END AS Type_Code,
               t.Attr AS RawAttr
        FROM tokens t
        WHERE NULLIF(t.Attr,'') IS NOT NULL
    )
    INSERT INTO dbo.TmX_Reference_Value_Type_Map (Reference_Value_Id, Reference_Type_Id, Active_Flag, Created_By)
    SELECT DISTINCT v.Reference_Value_Id, ty.Reference_Type_Id, 1, 'migration'
    FROM mapped m
    JOIN dbo.TmX_Reference_Type  ty ON ty.Type_Code = m.Type_Code
    JOIN dbo.TmX_Reference_Value v  ON v.Code = m.Code
                                    AND ((v.Lookup_Type = @countryLkp) OR (v.Lookup_Type IS NULL AND @countryLkp IS NULL))
    WHERE m.Type_Code IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Reference_Value_Type_Map mm
                      WHERE mm.Reference_Value_Id = v.Reference_Value_Id AND mm.Reference_Type_Id = ty.Reference_Type_Id);

    /* Report any unrecognized attribute tokens instead of silently dropping (plan §8.2). */
    ;WITH tokens AS (
        SELECT LTRIM(RTRIM(
                 CASE WHEN RIGHT(LTRIM(RTRIM(s.value)), 8) = ' Country'
                      THEN LEFT(LTRIM(RTRIM(s.value)), LEN(LTRIM(RTRIM(s.value))) - 8)
                      ELSE LTRIM(RTRIM(s.value)) END)) AS Attr
        FROM dbo.REPO_SANCTIONED_COUNTRIES c
        CROSS APPLY STRING_SPLIT(ISNULL(c.V_DATA_SOURCE, ''), '/') s
    )
    SELECT DISTINCT Attr AS Unrecognized_DataSource_Token
    INTO #unrecognized
    FROM tokens
    WHERE NULLIF(Attr,'') IS NOT NULL AND Attr <> 'Country'
      AND Attr NOT IN ('High Risk','Grey Listed','Black Listed','LandLocked','Land Locked','Tax Haven');

    DECLARE @unk int = (SELECT COUNT(*) FROM #unrecognized);
    IF @unk > 0
    BEGIN
        PRINT CONCAT('WARNING: ', @unk, ' unrecognized V_DATA_SOURCE token(s) were NOT mapped to an atomic type:');
        DECLARE @tok NVARCHAR(200);
        DECLARE cur CURSOR FAST_FORWARD FOR SELECT Unrecognized_DataSource_Token FROM #unrecognized;
        OPEN cur; FETCH NEXT FROM cur INTO @tok;
        WHILE @@FETCH_STATUS = 0 BEGIN PRINT CONCAT('   • ', @tok); FETCH NEXT FROM cur INTO @tok; END
        CLOSE cur; DEALLOCATE cur;
    END
    DROP TABLE #unrecognized;
END

/* 5c. Every migrated good → SCREENING_GOODS. */
IF OBJECT_ID('dbo.REPO_SCREENING_GOODS','U') IS NOT NULL AND @tGoods IS NOT NULL
    INSERT INTO dbo.TmX_Reference_Value_Type_Map (Reference_Value_Id, Reference_Type_Id, Active_Flag, Created_By)
    SELECT DISTINCT v.Reference_Value_Id, @tGoods, 1, 'migration'
    FROM dbo.TmX_Reference_Value v
    WHERE ((v.Lookup_Type = @goodsLkp) OR (v.Lookup_Type IS NULL AND @goodsLkp IS NULL))
      AND EXISTS (SELECT 1 FROM dbo.REPO_SCREENING_GOODS g WHERE LTRIM(RTRIM(g.V_GOOD_CODE)) = v.Code)
      AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Reference_Value_Type_Map m
                      WHERE m.Reference_Value_Id = v.Reference_Value_Id AND m.Reference_Type_Id = @tGoods);

/* 5d. Custom list memberships from TmX_Reference_List(_Value). */
IF OBJECT_ID('dbo.TmX_Reference_List_Value','U') IS NOT NULL AND OBJECT_ID('dbo.TmX_Reference_List','U') IS NOT NULL
    INSERT INTO dbo.TmX_Reference_Value_Type_Map (Reference_Value_Id, Reference_Type_Id, Active_Flag, Created_By)
    SELECT DISTINCT v.Reference_Value_Id, ty.Reference_Type_Id, rv.Active_Flag, 'migration'
    FROM dbo.TmX_Reference_List_Value rv
    JOIN dbo.TmX_Reference_List rl ON rl.Reference_List_ID = rv.Reference_List_ID
    JOIN dbo.TmX_Reference_Type ty ON ty.Type_Code = rl.List_Code
    JOIN dbo.TmX_Reference_Value v ON v.Code = LTRIM(RTRIM(rv.Value)) AND v.Lookup_Type IS NULL
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Reference_Value_Type_Map m
                      WHERE m.Reference_Value_Id = v.Reference_Value_Id AND m.Reference_Type_Id = ty.Reference_Type_Id);

DECLARE @cMaps int = (SELECT COUNT(*) FROM dbo.TmX_Reference_Value_Type_Map);
PRINT CONCAT('Membership rows after mapping: ', @cMaps);

/* ─────────────────────────────────────────────────────────────────────────
   6. INTEGRITY REPORT (plan §8.1) — Codes that are NOT a Hidden_Value in their
      Lookup_Type group would silently stop matching. Report, don't fail.
   ───────────────────────────────────────────────────────────────────────── */
DECLARE @drift int =
    (SELECT COUNT(*)
       FROM dbo.TmX_Reference_Value v
      WHERE v.Lookup_Type IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM dbo.TmX_Lookup l
                        WHERE l.Lookup_Type = v.Lookup_Type AND l.Hidden_Value = v.Code));
IF @drift > 0
    PRINT CONCAT('WARNING: ', @drift, ' reference value Code(s) do not resolve to a Hidden_Value in their Lookup_Type group — these will not match until corrected.');
ELSE
    PRINT 'Integrity OK: every domain-scoped Code resolves to a lookup Hidden_Value.';

COMMIT TRANSACTION;
GO

PRINT '2026_07_023_ReferenceListNormalizedModel.sql complete.';
GO
