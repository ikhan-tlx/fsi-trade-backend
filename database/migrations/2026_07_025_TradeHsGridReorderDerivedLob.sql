-- =========================================================================
-- 2026_07_025_TradeHsGridReorderDerivedLob.sql   —  WITHDRAWN. DO NOT RUN.
-- -------------------------------------------------------------------------
-- This migration was defective: its HS-grid re-sequence re-ordered fields it
-- should not have (it re-sequenced every child of the HS-Code column's parent,
-- which on LC Issuance is shared with the inline "General Info" fields) and it
-- keyed Field_Sequence on the non-unique Field_Label with no tie-breaker, so
-- the "Country" fields collapsed onto one sequence value ("Country, Country,
-- Country…") and the tab order was scrambled.
--
-- It has been REPLACED by:
--     2026_07_035_TradeHsGridReorderDerivedLob_SafeRebuild.sql
--
-- This file is intentionally a no-op so it can never re-apply the bad SQL.
-- It can be deleted from the repository — the fix lives entirely in 2026_07_035.
-- =========================================================================
PRINT '2026_07_025 is WITHDRAWN (no-op). Use 2026_07_035_TradeHsGridReorderDerivedLob_SafeRebuild.sql instead.';
GO
