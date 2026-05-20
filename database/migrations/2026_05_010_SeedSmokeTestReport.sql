-- =========================================================================
-- 2026_05_010_SeedSmokeTestReport.sql
--
-- Slice 7 — seeds a single, smallest-possible smoke-test report so the
-- new Reports stack can be exercised end-to-end without depending on the
-- bank's real report catalogue.
--
-- Inserts two rows:
--
--   1. TmX_Lookup REPORT_TYPE entry — admits sp_customer_report onto the
--      allowlist that ReportAllowlist.EnsureAllowedAsync enforces in the
--      Application layer.
--
--   2. TmX_Template — a minimal Liquid body that renders the SP's rows
--      as a simple HTML table. The new backend's RunReportHtmlCommand
--      handler matches Template_Name == ReportName.
--
-- Why sp_customer_report:
--   • Takes ZERO parameters — no LOVs, no date pickers, no filter wiring.
--   • Reads from tables we already use elsewhere (TmX_Customer_Master,
--     TmX_Transaction_Detail) so smoke-test data is guaranteed to exist.
--   • Returns plain columns (no JOIN-heavy subqueries) so the Liquid
--     template is easy to read.
--
-- Idempotent — both inserts guarded by NOT EXISTS. Safe to re-run.
--
-- Smoke-test recipe (run AFTER this migration):
--
--   1. POST /api/v1/Report/ReportHTML  (Bearer JWT required)
--      {
--        "ReportName":         "Smoke Customer Report",
--        "ReportVisibleName":  "Customer Smoke Test",
--        "StoredProcedure":    "sp_customer_report",
--        "Arguments":          {}
--      }
--      Expect: 200 with data.html containing a <table>.
--
--   2. PUT /api/v1/Report/GeneratePdfFromHtml
--      Body: { "ReportVisibleName": "Customer Smoke Test",
--              "HTML": "<the html from step 1>",
--              "PageOrientation": "Portrait" }
--      Expect: application/pdf binary stream (browser downloads
--              "Customer Smoke Test-<date>.pdf").
--
--   3. PUT /api/v1/Report/ReportExcel
--      Body: { "ReportVisibleName": "Customer Smoke Test",
--              "StoredProcedure":   "sp_customer_report",
--              "Arguments":         {} }
--      Expect: xlsx binary stream with a single sheet,
--              bold + frozen header row.
--
-- Run on:  ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;

-- -------------------------------------------------------------------------
-- 1. REPORT_TYPE lookup row — allowlist gate for sp_customer_report.
-- -------------------------------------------------------------------------
DECLARE @lookupType    nvarchar(100) = N'REPORT_TYPE';
DECLARE @visibleValue  nvarchar(999) = N'Customer Smoke Test';
DECLARE @hiddenValue   nvarchar(100) = N'sp_customer_report';
-- Description is parsed by the FE's parseReportConfig — first segment is
-- the SP name. For a zero-parameter report we just put the SP name and
-- nothing else.
DECLARE @description   nvarchar(500) = N'sp_customer_report';

-- Derive Tenant_ID + Locale_ID from any existing REPORT_TYPE row so the
-- new entry sits naturally alongside its siblings. Fall back to (1, 1)
-- when REPORT_TYPE is empty — common for fresh demo databases.
DECLARE @tenantId int = (
    SELECT TOP 1 Tenant_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = @lookupType
    ORDER  BY Lookup_ID
);
IF @tenantId IS NULL SET @tenantId = 1;

DECLARE @localeId int = (
    SELECT TOP 1 Locale_ID
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = @lookupType
    ORDER  BY Lookup_ID
);
IF @localeId IS NULL SET @localeId = 1;

DECLARE @sortOrder int = (
    SELECT ISNULL(MAX(Sort_Order), 0) + 10
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type = @lookupType
);

DECLARE @now            datetime      = GETUTCDATE();
DECLARE @nowAsNvarchar  nvarchar(100) = CONVERT(nvarchar(100), GETUTCDATE(), 121);
DECLARE @who            nvarchar(100) = N'System (Slice 7 smoke-test seed)';

PRINT CONCAT('Seeding REPORT_TYPE lookup with Tenant_ID=', @tenantId,
             ', Locale_ID=', @localeId,
             ', Sort_Order=', @sortOrder);

INSERT INTO dbo.TmX_Lookup
    (Tenant_ID,         Lookup_Type,      Visible_Value,   Hidden_Value,   Description,
     Is_Active,         Active_Flag,      User_Editable,   Sort_Order,
     Lookup_Name,
     Locale_ID,         Locale_Label,
     Created_By,        Created_Date,
     Last_Updated_By,   Last_Updated_Date)
SELECT
    @tenantId,          @lookupType,      @visibleValue,   @hiddenValue,   @description,
    1,                  1,                0,               @sortOrder,
    @hiddenValue,
    @localeId,          @visibleValue,
    @who,               @nowAsNvarchar,
    @who,               @now
WHERE NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Lookup
    WHERE  Lookup_Type    = @lookupType
      AND  Hidden_Value   = @hiddenValue
);

PRINT CONCAT('REPORT_TYPE seed: inserted ', @@ROWCOUNT, ' row(s).');

-- -------------------------------------------------------------------------
-- 2. TmX_Template row — Liquid body the new backend renders. Matched on
--    Template_Name == ReportName (the FE sends "Smoke Customer Report").
-- -------------------------------------------------------------------------
DECLARE @templateName        nvarchar(100)   = N'Smoke Customer Report';
DECLARE @templateDescription nvarchar(100)   = N'Slice 7 smoke-test report (zero-param sp_customer_report)';

-- Minimal Liquid body — renders rows in a table. Column names match what
-- sp_customer_report SELECTs ('Full_Legal_Name', 'Customer_Code', etc.).
-- DotLiquid sanitises dots in keys to underscores, so 'Customer_Code' is
-- already safe; the original 'Full_Legal_Name' is preserved as-is.
DECLARE @templateText nvarchar(max) = N'<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <title>Customer Smoke Test</title>
  <style>
    body { font: 12px/1.4 Arial, sans-serif; color: #222; }
    h1   { font-size: 16px; margin: 0 0 12px 0; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border: 1px solid #999; padding: 4px 8px; text-align: left; }
    th   { background: #eee; }
    .meta { color: #666; margin-bottom: 12px; }
  </style>
</head>
<body>
  <h1>Customer Smoke Test</h1>
  <div class="meta">Row count: {{ count }}</div>
  <table>
    <thead>
      <tr>
        <th>Customer Code</th>
        <th>Customer Name</th>
        <th>Full Legal Name</th>
        <th>Created By</th>
        <th>Transaction Id</th>
      </tr>
    </thead>
    <tbody>
      {% for row in rows %}
      <tr>
        <td>{{ row.Customer_Code }}</td>
        <td>{{ row.Customer_Name }}</td>
        <td>{{ row.Full_Legal_Name }}</td>
        <td>{{ row.Created_By }}</td>
        <td>{{ row.Transaction_Id }}</td>
      </tr>
      {% endfor %}
    </tbody>
  </table>
</body>
</html>';

PRINT 'Seeding TmX_Template (Template_Name = Smoke Customer Report)';

INSERT INTO dbo.TmX_Template
    (Template_Name,           Template_Description,    Template_Text,
     Template_Type_Lkp_ID,    Tenant_ID,               Product_Id,
     Is_Protected,            Password_Binding,
     Created_By,              Created_Date,
     Last_Updated_By,         Last_Updated_Date)
SELECT
    @templateName,            @templateDescription,    @templateText,
    NULL,                     @tenantId,               NULL,
    0,                        NULL,
    @who,                     @now,
    @who,                     @now
WHERE NOT EXISTS (
    SELECT 1
    FROM   dbo.TmX_Template
    WHERE  Template_Name = @templateName
);

PRINT CONCAT('TmX_Template seed: inserted ', @@ROWCOUNT, ' row(s).');

-- -------------------------------------------------------------------------
-- 3. Confirmation read-back — handy to eyeball post-run.
-- -------------------------------------------------------------------------
PRINT '';
PRINT 'REPORT_TYPE roster (post-seed):';
SELECT  Lookup_ID,
        Visible_Value,
        Hidden_Value,
        Description,
        Is_Active,
        Sort_Order
FROM    dbo.TmX_Lookup
WHERE   Lookup_Type = @lookupType
ORDER   BY Sort_Order, Lookup_ID;

PRINT '';
PRINT 'TmX_Template smoke row:';
SELECT  Template_ID,
        Template_Name,
        Template_Description,
        LEN(Template_Text) AS Template_Body_Length,
        Tenant_ID,
        Is_Protected,
        Created_Date
FROM    dbo.TmX_Template
WHERE   Template_Name = @templateName;

GO

PRINT 'Slice 7 smoke-test seed complete. Next: POST /api/v1/Report/ReportHTML with ReportName="Smoke Customer Report", StoredProcedure="sp_customer_report".';
