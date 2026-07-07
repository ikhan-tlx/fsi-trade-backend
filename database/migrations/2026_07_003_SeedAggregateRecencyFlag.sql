/* ============================================================================
   2026_07_003_SeedAggregateRecencyFlag.sql

   Seeds a real Phase-1 aggregate auto flag: "Dormant customer reactivation".
   Fires when a customer transacts after a long inactivity gap (the #39/#56
   dormancy pattern), using the DAYS_SINCE_LAST_ACTIVITY recency metric.

   Rule: Aggregate(DAYS_SINCE_LAST_ACTIVITY) GreaterThan 180  (days).
   Global scope (all products). Active. Aggregate mode (one flag per txn).

   Idempotent: keyed on Flag_Code = 'ARL-DORMANT180'. Safe to re-run.
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF EXISTS (SELECT 1 FROM dbo.TmX_Flag_Catalogue WHERE Flag_Code = 'ARL-DORMANT180')
BEGIN
    PRINT 'ARL-DORMANT180 already seeded — skipped.';
    RETURN;
END;

DECLARE @autoType INT = (SELECT TOP 1 Lookup_ID FROM dbo.TmX_Lookup WHERE Lookup_Type = 'FLAG_TYPE' AND Hidden_Value = 'AUTOMATED');
DECLARE @sev      INT = (SELECT TOP 1 Severity_Lkp_ID FROM dbo.TmX_Flag_Catalogue WHERE Severity_Lkp_ID IS NOT NULL);
IF @autoType IS NULL BEGIN PRINT 'AUTOMATED flag-type lookup not found — aborting.'; RETURN; END;

INSERT INTO dbo.TmX_Flag_Catalogue
    (Flag_Code, Flag_Name, Flag_Description, Flag_Type_Lkp_ID, Severity_Lkp_ID, Default_Weight,
     Raise_Per_Match, Active_Flag, Is_Built_In, Is_Deleted, Status, Version_No, Created_By, Created_Date)
VALUES
    ('ARL-DORMANT180', 'Dormant customer reactivation',
     'Customer transacts after 180+ days of inactivity (dormancy reactivation).',
     @autoType, @sev, 1.00, 0, 1, 0, 0, 'Active', 1, 'seed-2026_07_003', SYSUTCDATETIME());
DECLARE @flag INT = SCOPE_IDENTITY();

INSERT INTO dbo.TmX_Flag_Scope (Flag_ID, Product_ID, Tab_ID, Sort_Order, Active_Flag, Created_By, Created_Date)
VALUES (@flag, NULL, NULL, 0, 1, 'seed-2026_07_003', SYSUTCDATETIME());

INSERT INTO dbo.TmX_Flag_Rule
    (Flag_ID, Rule_Group, Evaluation_Order, Logical_Operator, Left_Source, Left_Field_Code,
     Operator, Right_Operand_Type, Static_Value, Handler_Key, Active_Flag, Created_By, Created_Date)
VALUES
    (@flag, 1, 1, 'AND', 'Aggregate', NULL,
     'GreaterThan', 'StaticValue', '180', 'DAYS_SINCE_LAST_ACTIVITY', 1, 'seed-2026_07_003', SYSUTCDATETIME());

PRINT CONCAT('Seeded aggregate flag ARL-DORMANT180 (Flag_ID=', @flag, ').');
GO
