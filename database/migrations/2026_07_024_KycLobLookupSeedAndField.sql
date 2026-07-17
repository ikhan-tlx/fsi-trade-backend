-- =========================================================================
-- 2026_07_024_KycLobLookupSeedAndField.sql
--
-- KYC Line-of-Business (LOB) — Phase 1 (LOB_DERIVATION_DESIGN_AND_PLAN.md):
--   1. Add JSON column dbo.TmX_Lookup.UDF (holds LOB extra attributes +
--      the HS-mapping spec { "hs":[{from,to}|{eq}], risk, score, ... }).
--   2. Seed lookup group 'LOB' (52 rows) from the RBA sheet — Visible/Hidden
--      + UDF metadata.
--   3. Convert the existing KYC "Line of Business" field (LineOfBus) from Text
--      to SearchableDropdown sourcing Field_Lookup_Type='LOB'. Legacy free-text
--      values simply render blank (re-selection) — no data rewrite.
--
-- Idempotent. Tenant_ID / Locale_ID derived from a sibling lookup row.
-- Run: sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -I -i "<path>\2026_07_024_KycLobLookupSeedAndField.sql"
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1. UDF column (additive, idempotent) ------------------------------------
IF COL_LENGTH('dbo.TmX_Lookup','UDF') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Lookup ADD UDF NVARCHAR(MAX) NULL;
    PRINT 'Added dbo.TmX_Lookup.UDF.';
END
ELSE PRINT 'dbo.TmX_Lookup.UDF already exists — skipped.';
GO

-- 2. Seed LOB lookup group -------------------------------------------------
DECLARE @tenantId int = (SELECT TOP 1 Tenant_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @tenantId IS NULL SET @tenantId = 1;
DECLARE @localeId int = (SELECT TOP 1 Locale_ID FROM dbo.TmX_Lookup ORDER BY Lookup_ID);
IF @localeId IS NULL SET @localeId = 1;
DECLARE @now datetime = GETUTCDATE();
DECLARE @nowStr nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who nvarchar(100) = N'System (LOB lookup seed)';

DECLARE @seed TABLE (Ord int IDENTITY(1,1), Hidden nvarchar(100), Visible nvarchar(400), Udf nvarchar(max));
INSERT INTO @seed (Hidden, Visible, Udf) VALUES
  (N'AGRICULTURAL_PRODUCE_TRADER', N'Agricultural Produce Trader', N'{"hs": [{"from": "01", "to": "14"}], "hsPrefix": "01\u201314", "chapterRange": "Agriculture", "goods": "Fruits, vegetables, cereals", "risk": "Medium", "score": 20, "notes": "Seasonal volume variance allowed"}'),
  (N'RICE_EXPORTER', N'Rice Exporter', N'{"hs": [{"eq": "1006"}], "hsPrefix": "1006", "chapterRange": "Cereals", "goods": "Rice", "risk": "Medium", "score": 20, "notes": "Country-specific routing checks"}'),
  (N'PROCESSED_FOOD_EXPORTER', N'Processed Food Exporter', N'{"hs": [{"from": "16", "to": "21"}], "hsPrefix": "16\u201321", "chapterRange": "Food Preparations", "goods": "Prepared food & beverages", "risk": "Medium", "score": 20, "notes": "Shelf-life vs transit validation"}'),
  (N'TEXTILE_MANUFACTURER_EXPORTER', N'Textile Manufacturer / Exporter', N'{"hs": [{"from": "50", "to": "63"}], "hsPrefix": "50\u201363", "chapterRange": "Textiles & Apparel", "goods": "Yarns, fabrics, home textiles", "risk": "Medium", "score": 20, "notes": "Validate HS prefix vs declared LoB"}'),
  (N'GARMENTS_EXPORTER', N'Garments Exporter', N'{"hs": [{"from": "61", "to": "62"}], "hsPrefix": "61\u201362", "chapterRange": "Apparel", "goods": "Clothing & accessories", "risk": "Medium", "score": 20, "notes": "Mismatch \u2192 Trade KYC deviation flag"}'),
  (N'CHEMICAL_IMPORTER_TRADER_INDUSTRIAL_COMMERCIAL', N'Chemical Importer / Trader (Industrial & Commercial)', N'{"hs": [{"from": "28", "to": "29"}, {"eq": "38"}], "hsPrefix": "28\u201329, 38", "chapterRange": "Chemicals", "goods": "Organic & inorganic chemicals", "risk": "High", "score": 30, "notes": "Dual-use screening; enhanced pricing"}'),
  (N'PHARMACEUTICAL_MANUFACTURER_EXPORTER_DISTRIBUTOR', N'Pharmaceutical Manufacturer / Exporter / Distributor', N'{"hs": [{"from": "29", "to": "30"}], "hsPrefix": "29\u201330", "chapterRange": "Pharmaceuticals & APIs", "goods": "Medicines, APIs, formulations", "risk": "High", "score": 30, "notes": "DRAP license mandatory; API vs output reconciliation"}'),
  (N'MEDICAL_EQUIPMENT_DEVICES_IMPORTER_DISTRIBUTOR', N'Medical Equipment & Devices Importer / Distributor', N'{"hs": [{"eq": "9018"}, {"eq": "9019"}, {"eq": "9021"}, {"eq": "9022"}, {"eq": "9402"}], "hsPrefix": "9018, 9019, 9021, 9022, 9402", "chapterRange": "Medical Instruments", "goods": "Dialysis, imaging, medical devices", "risk": "High", "score": 30, "notes": "Regulatory license; end-use & buyer verification"}'),
  (N'SURGICAL_INSTRUMENTS_MANUFACTURER_EXPORTER', N'Surgical Instruments Manufacturer / Exporter', N'{"hs": [{"eq": "9018"}], "hsPrefix": "9018", "chapterRange": "Medical Instruments", "goods": "Surgical & medical instruments", "risk": "High", "score": 30, "notes": "Capacity vs volume; OEM disclosure mandatory"}'),
  (N'STEEL_METAL_TRADER', N'Steel / Metal Trader', N'{"hs": [{"from": "72", "to": "83"}], "hsPrefix": "72\u201383", "chapterRange": "Base Metals", "goods": "Iron, steel, aluminum", "risk": "Medium", "score": 20, "notes": "Scrap trading flagged separately"}'),
  (N'SCRAP_IMPORTER_TRADER_BASE_METALS', N'Scrap Importer / Trader (Base Metals)', N'{"hs": [{"from": "72", "to": "81"}], "hsPrefix": "72\u201381", "chapterRange": "Base Metals & Articles", "goods": "Ferrous & non-ferrous scrap", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Mandatory ESI; scrap index benchmarking"}'),
  (N'PRECIOUS_METALS_TRADER', N'Precious Metals Trader', N'{"hs": [{"from": "7106", "to": "7112"}], "hsPrefix": "7106\u20137112", "chapterRange": "Precious metals & stones", "goods": "Gold, silver, platinum, bullion", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Source of funds; price benchmarking"}'),
  (N'BULLION_IMPORTER_EXPORTER', N'Bullion Importer / Exporter', N'{"hs": [{"eq": "7106"}, {"eq": "7108"}, {"eq": "7110"}], "hsPrefix": "7106, 7108, 7110", "chapterRange": "Precious metals", "goods": "Bullion (bars / coins)", "risk": "High (Auto)", "score": "Rule-Based", "notes": "EDD mandatory; senior approval"}'),
  (N'GEMS_STONES_TRADER', N'Gems & Stones Trader', N'{"hs": [{"from": "7101", "to": "7105"}], "hsPrefix": "7101\u20137105", "chapterRange": "Precious stones", "goods": "Diamonds, rubies, emeralds", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Valuation mismatch \u2192 auto red-flag"}'),
  (N'JEWELLERY_MANUFACTURER_EXPORTER', N'Jewellery Manufacturer / Exporter', N'{"hs": [{"from": "7113", "to": "7118"}], "hsPrefix": "7113\u20137118", "chapterRange": "Jewellery articles", "goods": "Jewellery & ornaments", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Weight\u2013value reconciliation"}'),
  (N'SCRAP_PRECIOUS_METALS_DEALER', N'Scrap Precious Metals Dealer', N'{"hs": [{"eq": "7112"}], "hsPrefix": "7112", "chapterRange": "Waste & scrap", "goods": "Precious metal scrap", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Laundering-prone; EDD mandatory"}'),
  (N'MACHINERY_IMPORTER', N'Machinery Importer', N'{"hs": [{"eq": "84"}], "hsPrefix": "84", "chapterRange": "Machinery", "goods": "Industrial machinery", "risk": "Medium", "score": 20, "notes": "Used machinery \u2192 enhanced checks"}'),
  (N'ELECTRICAL_EQUIPMENT_IMPORTER', N'Electrical Equipment Importer', N'{"hs": [{"eq": "85"}], "hsPrefix": "85", "chapterRange": "Electrical", "goods": "Electronics, equipment", "risk": "High", "score": 30, "notes": "Dual-use screening applicable"}'),
  (N'AUTO_PARTS_TRADER', N'Auto Parts Trader', N'{"hs": [{"eq": "87"}], "hsPrefix": "87", "chapterRange": "Vehicles", "goods": "Auto parts & vehicles", "risk": "Medium", "score": 20, "notes": "VIN / part consistency check"}'),
  (N'PETROLEUM_PRODUCTS_TRADER', N'Petroleum Products Trader', N'{"hs": [{"eq": "27"}], "hsPrefix": "27", "chapterRange": "Energy", "goods": "Oils, fuels, gas", "risk": "High", "score": 30, "notes": "Sanctions & pricing sensitivity"}'),
  (N'COAL_MINING_RESOURCE_EXTRACTION_COMPANY', N'Coal Mining & Resource Extraction Company', N'{"hs": [{"eq": "27"}], "hsPrefix": "27", "chapterRange": "Mineral Fuels", "goods": "Coal extraction & handling", "risk": "High", "score": 30, "notes": "Strategic natural resource"}'),
  (N'METAL_ORE_MINING_COMPANY', N'Metal Ore Mining Company', N'{"hs": [{"eq": "26"}], "hsPrefix": "26", "chapterRange": "Ores, Slag & Ash", "goods": "Iron, copper, bauxite ores", "risk": "High", "score": 30, "notes": "Export pricing & PF risk"}'),
  (N'PRECIOUS_METAL_MINING_COMPANY', N'Precious Metal Mining Company', N'{"hs": [{"eq": "26"}, {"eq": "71"}], "hsPrefix": "26, 71", "chapterRange": "Ores / Precious Metals", "goods": "Gold, silver, platinum mining", "risk": "High (Auto)", "score": "Rule-Based", "notes": "AML & PF vulnerability"}'),
  (N'INDUSTRIAL_MINERAL_MINING_COMPANY', N'Industrial Mineral Mining Company', N'{"hs": [{"eq": "25"}], "hsPrefix": "25", "chapterRange": "Salt, sulphur, stone", "goods": "Limestone, gypsum, phosphates", "risk": "Medium", "score": 20, "notes": "Volume vs capacity checks"}'),
  (N'RARE_EARTH_STRATEGIC_MINERAL_MINER', N'Rare Earth / Strategic Mineral Miner', N'{"hs": [{"eq": "26"}, {"eq": "28"}], "hsPrefix": "26, 28", "chapterRange": "Strategic minerals", "goods": "Lithium, uranium, rare earths", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Proliferation risk"}'),
  (N'MINING_PROJECT_COMPANY_SPV', N'Mining Project Company (SPV)', N'{"hs": [], "hsPrefix": "Multiple", "chapterRange": "Mixed", "goods": "Mine development & operations", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Project-based exposure"}'),
  (N'MINING_EPC_INFRASTRUCTURE_CONTRACTOR', N'Mining EPC / Infrastructure Contractor', N'{"hs": [{"eq": "73"}, {"eq": "84"}, {"eq": "85"}], "hsPrefix": "73, 84, 85", "chapterRange": "Construction / Machinery", "goods": "Mine construction & equipment", "risk": "High", "score": 30, "notes": "Capital goods scrutiny"}'),
  (N'ARMS_AMMUNITION_TRADER_MANUFACTURER', N'Arms & Ammunition Trader / Manufacturer', N'{"hs": [{"from": "9301", "to": "9307"}], "hsPrefix": "9301\u20139307", "chapterRange": "Arms & Ammunition", "goods": "Firearms, ammunition, parts", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Absolute High Risk; regulator license"}'),
  (N'EXPLOSIVES_PROPELLANTS_TRADER', N'Explosives & Propellants Trader', N'{"hs": [{"eq": "36"}], "hsPrefix": "36", "chapterRange": "Explosives", "goods": "Detonators, propellants", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Absolute High Risk"}'),
  (N'DEFENSE_MILITARY_EQUIPMENT_TRADER', N'Defense / Military Equipment Trader', N'{"hs": [{"eq": "87"}, {"eq": "88"}, {"eq": "93"}], "hsPrefix": "87, 88, 93", "chapterRange": "Defense", "goods": "Aircraft, armored vehicles", "risk": "High (Auto)", "score": "Rule-Based", "notes": "No downgrade permitted"}'),
  (N'DUAL_USE_STRATEGIC_GOODS_TRADER', N'Dual-Use / Strategic Goods Trader', N'{"hs": [{"eq": "84"}, {"eq": "85"}, {"eq": "90"}, {"eq": "28"}], "hsPrefix": "84, 85, 90, 28", "chapterRange": "Strategic / Dual-Use", "goods": "Avionics, surveillance, chemicals", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Sanctions & ESI mandatory"}'),
  (N'CONSTRUCTION_MATERIALS_TRADER', N'Construction Materials Trader', N'{"hs": [{"from": "68", "to": "70"}], "hsPrefix": "68\u201370", "chapterRange": "Construction", "goods": "Cement, glass, stone", "risk": "Medium", "score": 20, "notes": "Volume vs capacity check"}'),
  (N'CIVIL_CONSTRUCTION_CONTRACTOR', N'Civil Construction Contractor', N'{"hs": [{"from": "68", "to": "70"}, {"from": "72", "to": "73"}], "hsPrefix": "68\u201370, 72\u201373", "chapterRange": "Construction", "goods": "Cement, steel, aggregates", "risk": "Medium", "score": 20, "notes": "Project capacity validation"}'),
  (N'INFRASTRUCTURE_EPC_CONTRACTOR', N'Infrastructure EPC Contractor', N'{"hs": [{"from": "68", "to": "70"}, {"from": "72", "to": "73"}, {"from": "84", "to": "85"}], "hsPrefix": "68\u201370, 72\u201373, 84\u201385", "chapterRange": "Infrastructure", "goods": "Roads, bridges, utilities", "risk": "High", "score": 30, "notes": "Multi-HS exposure"}'),
  (N'INDUSTRIAL_PLANT_CONSTRUCTOR', N'Industrial Plant Constructor', N'{"hs": [{"from": "84", "to": "85"}, {"from": "72", "to": "73"}], "hsPrefix": "84\u201385, 72\u201373", "chapterRange": "Machinery & Metals", "goods": "Factories, processing plants", "risk": "High", "score": 30, "notes": "Vendor & machinery scrutiny"}'),
  (N'POWER_PLANT_ENERGY_EPC_CONTRACTOR', N'Power Plant / Energy EPC Contractor', N'{"hs": [{"eq": "84"}, {"eq": "85"}, {"eq": "73"}], "hsPrefix": "84, 85, 73", "chapterRange": "Energy Infrastructure", "goods": "Power plants, turbines", "risk": "High", "score": 30, "notes": "Strategic energy asset"}'),
  (N'TRANSMISSION_GRID_INFRASTRUCTURE_DEVELOPER', N'Transmission & Grid Infrastructure Developer', N'{"hs": [{"eq": "85"}, {"eq": "73"}], "hsPrefix": "85, 73", "chapterRange": "Electrical", "goods": "Transmission & substations", "risk": "High", "score": 30, "notes": "Strategic infrastructure flag"}'),
  (N'STRATEGIC_INFRASTRUCTURE_DEVELOPER_OPERATOR', N'Strategic Infrastructure Developer / Operator', N'{"hs": [], "hsPrefix": "Multiple", "chapterRange": "Mixed", "goods": "National infrastructure", "risk": "High (Auto)", "score": "Rule-Based", "notes": "SOE / strategic override"}'),
  (N'CPEC_GOVT_BACKED_PROJECT_COMPANY', N'CPEC / Govt-Backed Project Company', N'{"hs": [], "hsPrefix": "Multiple", "chapterRange": "Mixed", "goods": "Corridor & national projects", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Geopolitical exposure"}'),
  (N'STATE_OWNED_ENTERPRISE_SOE', N'State-Owned Enterprise (SOE)', N'{"hs": [], "hsPrefix": "Multiple", "chapterRange": "Mixed", "goods": "Govt-backed entities", "risk": "High (Auto)", "score": "Rule-Based", "notes": "PEP/SOE override"}'),
  (N'FREIGHT_FORWARDER', N'Freight Forwarder', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Routing & shipping coordination", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Trade facilitation risk"}'),
  (N'SHIPPING_AGENT', N'Shipping Agent', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Vessel & port services", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Layering risk"}'),
  (N'CLEARING_FORWARDING_C_F_AGENT', N'Clearing & Forwarding (C&F) Agent', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Customs clearance", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Valuation manipulation"}'),
  (N'CUSTOMS_BROKER', N'Customs Broker', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "HS classification", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Misclassification risk"}'),
  (N'WAREHOUSING_STORAGE_PROVIDER', N'Warehousing & Storage Provider', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Inventory custody", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Ownership ambiguity"}'),
  (N'COURIER_EXPRESS_CARGO_OPERATOR', N'Courier / Express Cargo Operator', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Small-value shipments", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Structuring risk"}'),
  (N'PRE_SHIPMENT_INSPECTION_SURVEYOR_ASSAYER', N'Pre-Shipment Inspection / Surveyor / Assayer', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Valuation & grading", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Price manipulation"}'),
  (N'TRADE_FINANCE_DOCUMENTATION_SERVICE_PROVIDER', N'Trade Finance / Documentation Service Provider', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "LC structuring, docs", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Document misuse risk"}'),
  (N'DIGITAL_TRADE_PLATFORM_AGGREGATOR', N'Digital Trade Platform / Aggregator', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Order & payment orchestration", "risk": "High (Auto)", "score": "Rule-Based", "notes": "Platform-based TBML"}'),
  (N'GENERAL_TRADER_MULTI_GOODS', N'General Trader (Multi-Goods)', N'{"hs": [], "hsPrefix": "Multiple", "chapterRange": "Mixed", "goods": "Multiple unrelated goods", "risk": "High (Auto)", "score": "Rule-Based", "notes": "ESI mandatory"}'),
  (N'OTHERS_NOT_LISTED', N'Others/Not Listed', N'{"hs": [], "hsPrefix": "Multiple", "chapterRange": "Mixed", "goods": "Multiple unrelated goods", "risk": "High (Auto)", "score": "Rule-Based", "notes": "ESI mandatory; detailed justification; senior approval required"}'),
  (N'HEALTHCARE_SERVICE_PROVIDER', N'Healthcare Service Provider', N'{"hs": [], "hsPrefix": "N/A", "chapterRange": "Services", "goods": "Hospitals, clinics, dialysis centers, diagnostic labs, medical treatment services", "risk": "Medium", "score": 20, "notes": "No trade in goods; HS code presence \u2192 auto red-flag; patient billing vs trade flows validation"}');

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID, Lookup_Type, Visible_Value, Hidden_Value, Description,
     Is_Active, Active_Flag, User_Editable, Sort_Order, Lookup_Name,
     Locale_ID, Locale_Label, UDF, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT
     @tenantId, N'LOB', s.Visible, s.Hidden, N'Line of Business (RBA).',
     1, 1, 1, s.Ord*10, s.Visible,
     @localeId, s.Visible, s.Udf, @who, @nowStr, @who, @now
FROM @seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Lookup l WHERE l.Lookup_Type=N'LOB' AND l.Hidden_Value=s.Hidden);
PRINT CONCAT('LOB lookup: inserted ', @@ROWCOUNT, ' row(s).');

-- Refresh UDF on existing rows (idempotent heal if re-run after edits)
UPDATE l SET l.UDF = s.Udf
FROM dbo.TmX_Lookup l JOIN @seed s ON l.Lookup_Type=N'LOB' AND l.Hidden_Value=s.Hidden
WHERE ISNULL(l.UDF,N'') <> s.Udf;
PRINT CONCAT('LOB lookup: UDF refreshed on ', @@ROWCOUNT, ' row(s).');
GO

-- 3. Convert KYC LineOfBus field -> SearchableDropdown sourcing 'LOB' -------
DECLARE @w nvarchar(100)=N'System (LOB field)'; DECLARE @n datetime=GETUTCDATE();
UPDATE dbo.TmX_Tenant_Field_Setup
   SET Field_Type_Lkp = 65,           -- SearchableDropdown
       Field_Lookup_Type = N'LOB',
       Last_Updated_By = @w, Last_Updated_Date = @n
 WHERE Field_Name = N'LineOfBus';
PRINT CONCAT('LineOfBus field converted: ', @@ROWCOUNT, ' row(s).');
GO

-- sanity
SELECT COUNT(*) AS Lob_Rows, COUNT(UDF) AS With_Udf FROM dbo.TmX_Lookup WHERE Lookup_Type=N'LOB';
SELECT Field_Name, Field_Type_Lkp, Field_Lookup_Type FROM dbo.TmX_Tenant_Field_Setup WHERE Field_Name=N'LineOfBus';
GO
PRINT '2026_07_024 complete.';
GO
