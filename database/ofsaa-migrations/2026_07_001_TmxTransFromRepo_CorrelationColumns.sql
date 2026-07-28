-- =====================================================================
-- OFSAA migration 2026_07_001 — TMX_TRANS_FROM_REPO correlation columns
-- Target: Oracle (OFSAA / MANTAS schema, reached via the app's ofsaafccm login).
-- =====================================================================
-- Symptom: "Proceed to FCCM" fails with
--     ORA-00904: "TRANSACTIONO": invalid identifier
-- on the correlation INSERT the app issues when redflagsCount > 0:
--     INSERT INTO TMX_TRANS_FROM_REPO
--         (CUSTOMERNAME, CLIENTREFNUM, TRANSACTION_ID, TRANSACTIONO)
--     VALUES (:p_customer, :p_clientRef, :p_txnId, :p_txnNum);
--
-- Root cause: TMX_TRANS_FROM_REPO is a BANK-created correlation table (not FCCM
-- out-of-the-box). Its base OFSAA-export shape carries only:
--     ID, TRANSACTION_ID, CNTRCT_INTRL_ID, PRODUCT_TYPE, HIT, PAYLOAD,
--     CREATE_TS, CASE_INTRL_ID, CATEGORY_CODE, MESSAGE_ID
-- The three columns the application writes — CUSTOMERNAME, CLIENTREFNUM,
-- TRANSACTIONO — are added out-of-band. They exist in DEV; a fresh/UAT OFSAA
-- schema does not have them, so the INSERT fails.
--
-- Provenance: this correlation INSERT (and these exact column names, incl. the
-- legacy one-'N' TRANSACTIONO) is the legacy behaviour — confirmed present in
-- the legacy `icbc_integration_test` implementation. The port is faithful; the
-- gap is purely the missing Oracle columns in non-dev environments.
--
-- Idempotent: -1430 ("column being added already exists") is swallowed, so this
-- is safe to re-run and safe to include in every OFSAA environment's deploy.
-- TRANSACTION_ID already exists in the base table and is NOT added here.
-- =====================================================================

PROMPT === OFSAA 2026_07_001: TMX_TRANS_FROM_REPO correlation columns ===

BEGIN
  EXECUTE IMMEDIATE q'~ALTER TABLE TMX_TRANS_FROM_REPO ADD ("CUSTOMERNAME" VARCHAR2(4000))~';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF;
END;
/
BEGIN
  EXECUTE IMMEDIATE q'~ALTER TABLE TMX_TRANS_FROM_REPO ADD ("CLIENTREFNUM" VARCHAR2(300))~';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF;
END;
/
BEGIN
  EXECUTE IMMEDIATE q'~ALTER TABLE TMX_TRANS_FROM_REPO ADD ("TRANSACTIONO" VARCHAR2(300))~';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF;
END;
/

-- Verify (expect the 3 new columns alongside the base 10):
--   SELECT column_name, data_type FROM all_tab_columns
--   WHERE  table_name = 'TMX_TRANS_FROM_REPO'
--   ORDER  BY owner, column_id;

PROMPT === Done. Re-run "Proceed to FCCM" for a flagged transaction to confirm. ===
