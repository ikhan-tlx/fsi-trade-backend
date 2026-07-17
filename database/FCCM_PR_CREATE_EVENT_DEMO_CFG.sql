-- =====================================================================
-- MIGRATION: PR_CREATE_EVENT_DEMO_CFG
-- Parameterised replica of PR_CREATE_EVENT_DEMO. Identical logic, but the
-- tenant/environment constants (jurisdiction, owner org/id, business domain,
-- event type, scenario class, case type, data origin, statuses, priorities,
-- contract event type) are IN parameters (defaulted to the original hardcoded
-- values). The application passes values from appsettings:Integration:TradeCaseDefaults.
-- The ORIGINAL PR_CREATE_EVENT_DEMO is left untouched. MANTAS-stripped,
-- unqualified (resolves in the login/atomic schema). CREATE OR REPLACE = idempotent.
-- =====================================================================
SET DEFINE OFF;

CREATE OR REPLACE PROCEDURE PR_CREATE_EVENT_DEMO_CFG (
    MESSAGE_ID   IN     VARCHAR2,
    PRODUCT      IN     VARCHAR2,
    NUM          IN     NUMBER,    
    RESID        OUT VARCHAR2,
    P_JRSDCN_CD          IN VARCHAR2 DEFAULT 'PAK',
    P_OWNER_ORG          IN VARCHAR2 DEFAULT 'ICBC',
    P_OWNER_ID           IN NUMBER   DEFAULT 10004,
    P_BUS_DMN            IN VARCHAR2 DEFAULT 'GEN',
    P_EVENT_TYPE         IN VARCHAR2 DEFAULT 'TBAML',
    P_SCENARIO_CLASS     IN VARCHAR2 DEFAULT 'TBML',
    P_CASE_TYPE          IN VARCHAR2 DEFAULT 'TBML_SURV',
    P_DATA_ORIGIN        IN VARCHAR2 DEFAULT 'DLY',
    P_EVENT_STATUS       IN VARCHAR2 DEFAULT 'CLS',
    P_EVENT_PRIORITY     IN VARCHAR2 DEFAULT 'HIGH',
    P_CASE_PRIORITY      IN VARCHAR2 DEFAULT 'High',
    P_CASE_SRC           IN VARCHAR2 DEFAULT 'CR',
    P_CASE_STATUS        IN VARCHAR2 DEFAULT 'NW',
    P_CNTRCT_EVENT_TYPE  IN VARCHAR2 DEFAULT 'ISSU')
IS
    ACC_TITLE VARCHAR2(250);

    TRADE_FIN_CNTRCT_SKEY NUMBER;
    L_CONTRACT_REC                     LC_TRN_DET_STAGING%ROWTYPE;

    L_EVENT_REC                        KDD_CASE_TRD_FIN_CNTRCT_EVT%ROWTYPE;

    L_CUSTOMER_REC                     CUST_MASTER_STAGING%ROWTYPE;

    L_CUST_BANKING_REC                 CUST_BANKING_STAGING%ROWTYPE;

    L_BENE_REC                         BENEFICIARY_DETAIL_STAGING%ROWTYPE;

    L_CASE_TRD_FIN_PARTY_SEQ_ID        NUMBER;

    L_N_EVENT_SKEY                     VARCHAR2 (10);

    L_N_EVENT_CORRELATION_SKEY         NUMBER;

	L_N_EVENT_CORR_SKEY		   NUMBER;

    L_N_RUN_SKEY                       VARCHAR2 (10);

    L_CASE_ID                          VARCHAR2 (10);

    L_V_EVENT_CD                       VARCHAR2 (10);

    L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID   NUMBER;

    L_CASE_TRD_FIN_GOOD_SRVC_SEQ_ID    NUMBER;

    L_CASE_TRD_FIN_CNTRCT_SEQ_ID       NUMBER;

    L_N_EVENTS_ENTITY_MAP_SEQ          NUMBER;

    L_N_ENTITY_SKEY                    NUMBER;

    CURR_DATE                          DATE;

    L_CONTRACT_TRACKING                VARCHAR2 (20);

       L_N_EVENT_SKEY_B                     VARCHAR2 (10);

   L_N_RUN_SKEY_B                       VARCHAR2 (10);

   L_N_EVENTS_ENTITY_MAP_SEQ_B          NUMBER;
  CUST_RISK VARCHAR2(15);

BEGIN
    SELECT CM_CASES_SEQ.NEXTVAL INTO L_CASE_ID FROM DUAL; 

    SELECT CM_EVENT_LOOKUP_SEQ.NEXTVAL INTO L_N_EVENT_SKEY FROM DUAL;

    SELECT CM_BATCH_RUN_SEQ.NEXTVAL INTO L_N_RUN_SKEY FROM DUAL;

    SELECT SYSDATE INTO CURR_DATE FROM DUAL;

    SELECT CM_EVENTS_ENTITY_MAP_SEQ.NEXTVAL
      INTO L_N_EVENTS_ENTITY_MAP_SEQ
      FROM DUAL;

     SELECT CM_TRD_FIN_CNTRCT_LOOKUP_SEQ.NEXTVAL INTO TRADE_FIN_CNTRCT_SKEY FROM DUAL;
L_N_ENTITY_SKEY := TRADE_FIN_CNTRCT_SKEY;
--    SELECT NVL(MAX (N_ENTITY_SKEY),0)+1 INTO L_N_ENTITY_SKEY FROM fcc_event_entity_map;

    --   SELECT   MAX (N_EVENTS_ENTITY_MAP_SEQ)
    --     INTO   L_N_EVENTS_ENTITY_MAP_SEQ
    --     FROM   fcc_event_entity_map
    --    WHERE       N_EVENT_SKEY = L_N_EVENT_SKEY
    --            AND N_RUN_SKEY = L_N_RUN_SKEY
    --            AND V_ENTITY_TYPE = 'CUSTOMER';
    SELECT CM_CORRELATION_EVENT_MAP_SEQ.NEXTVAL
      INTO L_N_EVENT_CORRELATION_SKEY
      FROM DUAL;

	  SELECT CM_EVENT_CORRELATION_SEQ.NEXTVAL-1
      INTO L_N_EVENT_CORR_SKEY
      FROM DUAL;

    --   SELECT   MAX (N_EVENT_CORRELATION_SKEY)
    --     INTO   L_N_EVENT_CORRELATION_SKEY
    --     FROM   FCC_CORRELATION_EVENT_MAP;
    SELECT *
      INTO L_CONTRACT_REC
      FROM LC_TRN_DET_STAGING LC
     WHERE MESSAGE_ID = (SELECT MESSAGE_ID FROM RT_CASE_TRACKING);

    -----POPULATING KDD_CASE_TRD_FIN_CNTRCT EVENT------
    SELECT CM_TRD_FIN_CNTRCT_EVT_SEQ.NEXTVAL
      INTO L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID
      FROM DUAL;

    IF NUM = 1
    THEN
        IF PRODUCT = 'LC'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            EXPIRY_PLACE,
                            CNTRCT_AMT,
                            SHPMT_FRM,
                            SHPMT_TO,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            SHPMT_DTLS,
                            ADDL_CONDITION_TX,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            INSTR_TX,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         ('CA' || ((L_CASE_ID))),
                         L_CONTRACT_REC.LCID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         P_CNTRCT_EVENT_TYPE,
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'LC',
                         'ILC',
                         L_CONTRACT_REC.EXPIRYPLACE,
                         L_CONTRACT_REC.LCAMOUNT,
                         L_CONTRACT_REC.SHIPDESPATCH,
                         L_CONTRACT_REC.FINALDESTINATION,
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.OTHERDESPATCHDET,
                         L_CONTRACT_REC.ADDITIONALCOND,
                         L_CONTRACT_REC.LCID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.COMMODITY,
                         L_CONTRACT_REC.INSTRUCTIONS,
                         L_CONTRACT_REC.LCAMOUNT);
        ELSIF PRODUCT = 'MD'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         ('CA' || (L_CASE_ID)),
                         L_CONTRACT_REC.MDID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         P_CNTRCT_EVENT_TYPE,
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'MD',
                         'MD',
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.MDID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.GOODSDESCRIPTION);
        ELSIF PRODUCT = 'WB'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            CNTRCT_AMT,
                            DISCHRG_PORT,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            GOOD_SRVC_CT,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         ('CA' || (L_CASE_ID)),
                         L_CONTRACT_REC.WBID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         P_CNTRCT_EVENT_TYPE,
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'WB',
                         'WB',
                         L_CONTRACT_REC.TOTEFORMAMT,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.WBID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.COMMODITY,
                         L_CONTRACT_REC.HSQUANT,
                         L_CONTRACT_REC.TOTEFORMAMT);
        ELSIF PRODUCT = 'DR'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            CNTRCT_AMT,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            SHPMT_TO,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            GOOD_SRVC_CT,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         ('CA' || (L_CASE_ID)),
                         L_CONTRACT_REC.DRID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         P_CNTRCT_EVENT_TYPE,
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'DR',
                         'DR',
                         L_CONTRACT_REC.DOCAMNT,
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.FINALDESTINATION,
                         L_CONTRACT_REC.DRID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.GOODSDESCRIPTION,
                         L_CONTRACT_REC.HSQUANT,
                         L_CONTRACT_REC.DOCAMNT);
        ELSIF PRODUCT = 'FT'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            CNTRCT_AMT,
                            SHPMT_TO,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            GOOD_SRVC_CT,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         ('CA' || (L_CASE_ID)),
                         L_CONTRACT_REC.FTID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         P_CNTRCT_EVENT_TYPE,
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'FT',
                         'FT',
                         L_CONTRACT_REC.CREDITAMNT,
                         L_CONTRACT_REC.FINALDESTINATION,
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.FTID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.GOODSDESCRIPTION,
                         L_CONTRACT_REC.HSQUANT,
                         L_CONTRACT_REC.CREDITAMNT);
        END IF;

        SELECT *
          INTO L_EVENT_REC
          FROM KDD_CASE_TRD_FIN_CNTRCT_EVT
         WHERE CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID =
               L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID;

        SELECT CM_TRD_FIN_CNTRCT_SEQ.NEXTVAL
          INTO L_CASE_TRD_FIN_CNTRCT_SEQ_ID
          FROM DUAL;

        INSERT INTO KDD_CASE_TRD_FIN_CNTRCT (CASE_TRD_FIN_CNTRCT_SEQ_ID,
                                             CASE_INTRL_ID,
                                             TRD_FIN_CNTRCT_INTRL_ID,
                                             LAST_EVENT_TYPE_CD,
                                             LAST_UPDATE_DT,
                                             LAST_UPDATE_TM,
                                             LAST_UPDATE_UTC_TM,
                                             LAST_UPDATE_UTC_DT,
                                             IRREVOCABLE_FL,
                                             LAST_TFRABLE_FL,
                                             STANDBY_FL,
                                             REVOLVING_FL,
                                             PART_SHPMT_FL,
                                             TRANS_SHPMT_FL,
                                             TOT_UNCNFRMND_AMD_CT,
                                             TOT_CNFRMND_AMD_CT,
                                             TOT_REJ_AMD_CT,
                                             AMD_EVENT_CT,
                                             PRDCT_TYPE_CD,
                                             PRDCT_SUB_TYPE_CD,
                                             LAST_EXPIRY_PLACE,
                                             LAST_CNTRCT_AMT,
                                             LAST_SHPMT_FRM,
                                             LAST_SHPMT_TO,
                                             LAST_LOAD_PORT,
                                             LAST_DISCHRG_PORT,
                                             LAST_SHPMT_DTLS,
                                             LAST_CNTRCT_ACTVY_AMT,
                                             LAST_SHPT_CNTRY_CD,
                                             LAST_SHPF_CNTRY_CD,
                                             LAST_ADDL_CONDITION_TX,
                                             LAST_INSTR_TX,
                                             LAST_PARTY_CNTRCT_INTRL_ID,
                                             SRC_SYS_CD,
                                             LAST_PRCSNG_BATCH_NM,
                                             LAST_DATA_DUMP_DT,
                                             LAST_GOOD_SRVC_DESC_TX)
             VALUES ((L_CASE_TRD_FIN_CNTRCT_SEQ_ID),
                     L_EVENT_REC.CASE_INTRL_ID,
                     L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                     L_EVENT_REC.CNTRCT_EVENT_TYPE_CD,
                     L_EVENT_REC.CNTRCT_EVENT_DT,
                     L_EVENT_REC.CNTRCT_EVENT_TM,
                     L_EVENT_REC.CNTRCT_EVENT_UTC_TM,
                     L_EVENT_REC.CNTRCT_EVENT_UTC_DT,
                     'N',
                     'N',
                     'N',
                     'N',
                     'N',
                     'N',
                     0,
                     0,
                     0,
                     0,
                     L_EVENT_REC.PRDCT_TYPE_CD,
                     L_EVENT_REC.PRDCT_SUB_TYPE_CD,
                     L_EVENT_REC.EXPIRY_PLACE,
                     L_EVENT_REC.CNTRCT_AMT,
                     L_EVENT_REC.SHPMT_FRM,
                     L_EVENT_REC.SHPMT_TO,
                     L_EVENT_REC.LOAD_PORT,
                     L_EVENT_REC.DISCHRG_PORT,
                     L_EVENT_REC.SHPMT_DTLS,
                     L_EVENT_REC.CNTRCT_ACTVY_AMT,
                     L_EVENT_REC.SHPT_CNTRY_CD,
                     L_EVENT_REC.SHPF_CNTRY_CD,
                     L_EVENT_REC.ADDL_CONDITION_TX,
                     L_EVENT_REC.INSTR_TX,
                     L_EVENT_REC.PARTY_CNTRCT_INTRL_ID,
                     L_EVENT_REC.SRC_SYS_CD,
                     L_EVENT_REC.PRCSNG_BATCH_NM,
                     L_EVENT_REC.DATA_DUMP_DT,
                     L_EVENT_REC.GOOD_SRVC_DESC_TX);

        SELECT CM_TRD_FIN_GOOD_SRVC_SEQ.NEXTVAL
          INTO L_CASE_TRD_FIN_GOOD_SRVC_SEQ_ID
          FROM DUAL;

        INSERT INTO KDD_CASE_TRD_FIN_GOOD_SRVC (
                        CASE_TRD_FIN_GOOD_SRVC_SEQ_ID,
                        CASE_INTRL_ID,
                        TRD_FIN_CNTRCT_INTRL_ID,
                        EVENT_NB,
                        GOOD_SRVC_DESC_TX,
                        GOOD_SRVC_CD,
                        UNIT_PR,
                        UNIT_PR_CRNCY_CD,
                        UNIT_QT,
                        SRC_SYS_CD,
                        PRCSNG_BATCH_NM,
                        DATA_DUMP_DT)
             VALUES ((L_CASE_TRD_FIN_GOOD_SRVC_SEQ_ID),
                     L_EVENT_REC.CASE_INTRL_ID,
                     L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                     L_EVENT_REC.CNTRCT_EVENT_NB,
                     L_EVENT_REC.GOOD_SRVC_DESC_TX,
                     L_CONTRACT_REC.HSCODE,
                     L_CONTRACT_REC.SELLUNITPRICE,
                     L_CONTRACT_REC.UNITCURRENCY,
                     L_EVENT_REC.GOOD_SRVC_CT,
                     P_DATA_ORIGIN,
                     P_DATA_ORIGIN,
                     CURR_DATE);

        --   SELECT   CM_TRD_FIN_PARTY_SEQ.NEXTVAL
        --     INTO   L_CASE_TRD_FIN_PARTY_SEQ_ID
        --     FROM   DUAL;



        SELECT *
          INTO L_BENE_REC
          FROM BENEFICIARY_DETAIL_STAGING BD
         WHERE BD.MESSAGE_ID = (SELECT MESSAGE_ID FROM RT_CASE_TRACKING);

        IF PRODUCT = 'LC'
        THEN
            IF L_BENE_REC.BENEFICIARY IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'BENE',
                             L_BENE_REC.BENEFICIARY,
                             L_BENE_REC.BENEFICIARY,
                             'N',
                             L_BENE_REC.BENEFICIARYCUSTNO,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;

            IF L_BENE_REC.ADVISETHRU IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'ADVS',
                             L_BENE_REC.ADVISETHRU,
                             L_BENE_REC.ADVISETHRU,
                             'N',
                             L_BENE_REC.ADVISETHRUCUSTNO,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;

            IF L_BENE_REC.ADVISINGBK IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'RCV',
                             L_BENE_REC.ADVISINGBK,
                             L_BENE_REC.ADVISINGBK,
                             'N',
                             L_BENE_REC.ADVISINGBKCUSTNO,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        ELSIF PRODUCT = 'DR'
        THEN
            IF L_BENE_REC.SENDERCORR IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'SEND',
                             L_BENE_REC.SENDERCORR,
                             L_BENE_REC.SENDERCORR,
                             'N',
                             L_BENE_REC.SENDERCORR,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            ELSIF L_BENE_REC.RECEIVERSBANK IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'RCV',
                             L_BENE_REC.RECEIVERSBANK,
                             L_BENE_REC.RECEIVERSBANK,
                             'N',
                             L_BENE_REC.RECEIVERSCORR,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            ELSIF L_BENE_REC.INTERMEDBANK IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'GUAR',
                             L_BENE_REC.INTERMEDBANK,
                             L_BENE_REC.INTERMEDBANK,
                             'N',
                             L_BENE_REC.INTERMEDBANK,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        --ELSIF PRODUCCT = 'WB'     NEED TO UNDERSTAND MAPPING
        ELSIF PRODUCT = 'MD'
        THEN
            IF L_BENE_REC.BENEFICIARY IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'BENE',
                             L_BENE_REC.BENEFCUST1,
                             L_BENE_REC.BENEFCUST1,
                             'N',
                             L_BENE_REC.BENEFCUST1,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        ELSIF PRODUCT = 'FT'
        THEN
            IF L_BENE_REC.BENEFICIARYCUST IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'BENE',
                             L_BENE_REC.BENEFICIARYCUST,
                             L_BENE_REC.BENEFICIARYCUST,
                             'N',
                             L_BENE_REC.BENEFICIARYID,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        END IF;


--       populating amount to be displayed on the customer's tab
MERGE INTO FCC_TRADE_FIN_CNTRCT_EVNT USING DUAL ON (TRD_FIN_CNTRCT_INTRL_ID = COALESCE(L_CONTRACT_REC.LCID,L_CONTRACT_REC.WBID,L_CONTRACT_REC.FTID,L_CONTRACT_REC.DRID,L_CONTRACT_REC.MDID
))--L_CONTRACT_REC.LCID)
WHEN MATCHED THEN 
UPDATE SET MIS_DATE = SYSDATE, TRADEFINANCECONTRACT_SKEY=L_N_ENTITY_SKEY
WHEN NOT MATCHED THEN
INSERT (TRADEFINANCECONTRACT_SKEY,TRD_FIN_CNTRCT_INTRL_ID,GOOD_SRVC_DESC_TX,CNTRCT_ACTVY_AMT,MIS_DATE,DATA_ORIGIN,ACTVY_RISK_NB,LAST_ACTVY_RISK_NB) VALUES
(L_N_ENTITY_SKEY,  COALESCE(L_CONTRACT_REC.LCID,L_CONTRACT_REC.WBID,L_CONTRACT_REC.FTID,L_CONTRACT_REC.DRID,L_CONTRACT_REC.MDID
), L_CONTRACT_REC.COMMODITY,  COALESCE (L_CONTRACT_REC.DEBITAMOUNT,L_CONTRACT_REC.LCAMOUNT ,L_CONTRACT_REC.TOTEFORMAMT ,L_CONTRACT_REC.CREDITAMNT,L_CONTRACT_REC.DOCAMNT ,L_CONTRACT_REC.EIFAMNT),SYSDATE,P_DATA_ORIGIN,1,1);

        L_N_RUN_SKEY_B := L_N_RUN_SKEY;
    L_N_EVENT_SKEY_B := L_N_EVENT_SKEY;
    L_N_EVENTS_ENTITY_MAP_SEQ_B := L_N_EVENTS_ENTITY_MAP_SEQ;
    SELECT TITLE INTO ACC_TITLE FROM EVENT_STAGING WHERE MSG_ID = MESSAGE_ID AND ROWNUM=1;
    FOR EVENT_RECORD IN (SELECT MSG_ID,INPUT_NAME,INPUT_MATCH,DESCRIPTION,TITLE FROM EVENT_STAGING WHERE MSG_ID = MESSAGE_ID)
    LOOP
    IF ACC_TITLE IS NULL THEN
    SELECT EVENT_RECORD.TITLE INTO ACC_TITLE FROM DUAL;
   END IF;
  DBMS_OUTPUT.PUT_LINE(CONCAT('L_N_RUN_SKEY: ',L_N_RUN_SKEY_B));
        ----POPULATING EVENT TABLE-----
        INSERT INTO FCC_EVENTS (N_EVENT_SKEY,
                                N_RUN_SKEY,
                                D_MIS_DATE,
                                V_EVENT_CD,
                                V_EVENT_TYPE,
                                V_DATA_ORIGIN,
                                V_STATUS_CD,
                                D_EVENT_CREATED_DATE,
                                N_EVENT_SCORE,
                                V_JURISDICTION_CD,
                                V_BUSINESS_DOMAIN_CD,
                                V_EVENT_PRIORITY,
                                V_COMMENTS,
                                V_SCENARIO_CLASS)
             VALUES ((L_N_EVENT_SKEY_B),
                     (L_N_RUN_SKEY),
                     CURR_DATE,
                     ('C' || L_N_EVENT_SKEY_B),
                     P_EVENT_TYPE,
                     P_DATA_ORIGIN,
                     P_EVENT_STATUS,
                     CURR_DATE,
                     0,
                     P_JRSDCN_CD,
                     P_BUS_DMN,
                     P_EVENT_PRIORITY,
                     'STAGINGPROCCOMMENT',
                     P_SCENARIO_CLASS);

        -----POPULATING fcc_event_entity_map-------
        INSERT INTO fcc_event_entity_map (N_RUN_SKEY,
                                          N_EVENT_SKEY,
                                          N_ENTITY_SKEY,
                                          V_ENTITY_TYPE,
                                          V_FOCUS_FLAG,
                                          V_DIRECT_ENTITY_FLAG,
                                          V_ENTITY_NAME,
                                          N_EVENTS_ENTITY_MAP_SEQ,
                                          V_ENTITY_CD)
             VALUES ((L_N_RUN_SKEY),
                     (L_N_EVENT_SKEY_B),
                     (L_N_ENTITY_SKEY),
                        NVL(EVENT_RECORD.TITLE,'N/A'), --EVENT_RECORD.INPUT_NAME,
                     'Y',
                     'Y',
                    EVENT_RECORD.INPUT_NAME,  --'ENTITY_NAME',
                     ---NEEDS TO BE UPDATED FROM LC
                     (L_N_EVENTS_ENTITY_MAP_SEQ),
                     EVENT_RECORD.INPUT_MATCH);

        ----Populating fcc_event_details-----
        INSERT INTO fcc_event_details (N_RUN_SKEY,
                                       N_EVENT_SKEY,
                                       V_EVENT_REASON,
                                       N_SCENARIO_SKEY)
             VALUES ((L_N_RUN_SKEY),
                     (L_N_EVENT_SKEY_B),
                     EVENT_RECORD.DESCRIPTION,
                     2);

        ----POPULATING FCC_CORRELATION_EVENT_MAP -----
        INSERT INTO FCC_CORRELATION_EVENT_MAP (N_CORR_EVENT_MAP_SKEY,
                                               N_EVENT_CORRELATION_SKEY,
                                               N_EVENT_SKEY,
                                               D_LINK_CREATION_DATE,
                                               N_EVENT_PRIORITY,
                                               N_RUN_SKEY)
             VALUES ((L_N_EVENT_CORRELATION_SKEY),
                     (L_N_EVENT_CORR_SKEY),
                     (L_N_EVENT_SKEY_B),
                     CURR_DATE,
                     1,
                     (L_N_RUN_SKEY));

               -- This will bypass the PRODUCTIVE/NON-PRODUCTIVE 
         INSERT INTO FCC_EVENT_INVESTIGATION_STATUS (
        N_EVENT_SKEY,
        N_STATUS_ID,
        D_ACTION_DATE,
        N_ACTION_BY) VALUES (
        L_N_EVENT_SKEY_B, 
        1, 
        sysdate, 
        P_OWNER_ID);

        SELECT   CM_EVENTS_ENTITY_MAP_SEQ.NEXTVAL
        INTO   L_N_EVENTS_ENTITY_MAP_SEQ_B
        FROM   DUAL;

        SELECT   CM_EVENT_LOOKUP_SEQ.NEXTVAL INTO L_N_EVENT_SKEY_B FROM DUAL;

        SELECT   CM_BATCH_RUN_SEQ.NEXTVAL INTO L_N_RUN_SKEY_B FROM DUAL;

END LOOP;

        ---POPULATING FCC_PRECASE_CASE_MAP------
        INSERT INTO FCC_PRECASE_CASE_MAP (N_EVENT_CORRELATION_SKEY,
                                          V_CASE_ID,
                                          V_CASE_TYPE,
                                          N_RUN_SKEY,
                                          V_JURISDICTION_CD,
                                          V_BUSINESS_DOMAIN_CD,
                                          V_CORRELATION_TYPE)
             VALUES ((L_N_EVENT_CORR_SKEY),
                     ('CA' || (L_CASE_ID)),
                     P_CASE_TYPE,
                     (L_N_RUN_SKEY),
                     P_JRSDCN_CD,
                     P_BUS_DMN,
                     NULL);


        ---------populating kdd_cases table-------------
        INSERT INTO KDD_CASES (CASE_INTRL_ID,
                               CASE_REVIEW_ID,
                               CASE_TITL_NM,
                               CREATED_TS,
                               LAST_UPDATED_BY_ID,
                               CREATE_BY_ID,
                               OWNER_ID,
                               ASSIGNED_TO_ID,
                               ASSIGNED_BY_ID,
                               CONFIDENTIAL_FL,
                               CASE_ASSIGNED_TS,
                               CASE_DESC_TX,
                               STATUS_CD,
                               LAST_ACTION_NAME,
                               ORIG_OWNER_ID,
                               OWNER_ORG,
                               JRSDCN_CD,
                               CASE_SRC_CD,
                               PRIORITY_CD,
                               BUS_DMN_ST,
                               SCORE_CT,
                               PRCSNG_CMPLT_FL,
                               CASE_TYPE_SUBTYPE_CD,
                               CASE_TYPE_CD,
                               AGE,
                               PURGE_RULE_OVERRIDE_FL)
             VALUES (('CA' || (L_CASE_ID)),
                     (L_CASE_ID),
                     NVL(ACC_TITLE,'N/A'),
                     CURR_DATE,
                     1,
                     1,
                     P_OWNER_ID,
                     P_OWNER_ID,
                     1,
                     'Y',
                     CURR_DATE,
                     'REAL-TIME-CASE',
                     P_CASE_STATUS,
                     'Create Case',
                     P_OWNER_ID,
                     P_OWNER_ORG,
                     P_JRSDCN_CD,
                     P_CASE_SRC,
                     P_CASE_PRIORITY,
                     'c',
                     0,
                     'Y',
                     P_CASE_TYPE,
                     P_CASE_TYPE,
                     0,
                     'Y');
        INSERT INTO CASEIDTOTRANSACTION VALUES(('CA' || (L_CASE_ID)),L_BENE_REC.TRANSACTIONNUMBER);
        ------------------------
        SELECT ('CA' || (L_CASE_ID)) INTO RESID FROM DUAL;

        -------POPULATING CUSTOMER TABLE------------
        SELECT *
          INTO L_CUSTOMER_REC
          FROM CUST_MASTER_STAGING
         WHERE MESSAGE_ID = (SELECT MESSAGE_ID FROM RT_CASE_TRACKING);
-- fetching risk from the custom table

        BEGIN
        SELECT NVL(RISK,'N/A') INTO CUST_RISK FROM CUST_RISK_MAP_CSTM WHERE CUST_INTRL_ID = (
                SELECT NVL(COALESCE( L_CUSTOMER_REC.APPLICANTCUSTNO, L_CUSTOMER_REC.DRAWEECUSTNO, L_CUSTOMER_REC.PRESENTORCUST, L_CUSTOMER_REC.ORDERINGCUST, L_CUSTOMER_REC.CUSTOMER, L_CUSTOMER_REC.CUSTOMERID),'N/A') FROM DUAL);

              EXCEPTION WHEN OTHERS THEN
              CUST_RISK := 'N/A';
             END;
        IF PRODUCT = 'LC'
        THEN
            IF L_CUSTOMER_REC.APPLICANTCUSTNO IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.APPLICANTCUSTNO,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.APPLICANT);
            END IF;

            IF L_CUSTOMER_REC.DRAWEECUSTNO IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.DRAWEECUSTNO,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.DRAWEE);
            END IF;
        ELSIF PRODUCT = 'DR'
        THEN
            IF L_CUSTOMER_REC.PRESENTORCUST IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.PRESENTORCUST,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.PRESENTOR);
            END IF;
        ELSIF PRODUCT = 'FT'
        THEN
            IF L_CUSTOMER_REC.ORDERINGCUST IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.ORDERINGCUST,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.ORDERINGBANK);
            END IF;

            IF L_CUSTOMER_REC.CUSTOMER IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.CUSTOMER,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.CUSTOMER);
            END IF;
        ELSIF PRODUCT = 'MD'
        THEN
            IF L_CUSTOMER_REC.CUSTOMER IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.CUSTOMER,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.CUSTOMER);
            END IF;
        ELSIF PRODUCT = 'WB'
        THEN
            IF L_CUSTOMER_REC.CUSTOMERID IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.CUSTOMERID,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             ('CA' || (L_CASE_ID)),
                             'c',
                             L_CUSTOMER_REC.CUSTOMERID);

                EXECUTE IMMEDIATE 'TRUNCATE TABLE RT_CASE_TRACKING';

                COMMIT;
            END IF;
        END IF;
    ---------------AMMENDMENTS-----------------
    ELSE
        SELECT CASE_INTRL_ID
          INTO L_CASE_ID
          FROM KDD_CASE_TRD_FIN_CNTRCT_EVT
         WHERE TRD_FIN_CNTRCT_INTRL_ID = L_CONTRACT_REC.LCID AND ROWNUM = 1;
---------mapping case_action ---------

--        INSERT INTO KDD_CASE_ACTIONS (
--        ACTION_SEQ_ID,
--        ACTION_BY_ID,
--        ACTION_TS,
--        STATUS_CD,
--        CASE_INTRL_ID,
--        ACTION_ID
--        )
--        VALUES (
--        cm_case_action_seq.nextval,
--        '10008',
--        sysdate,
--        'INV',
--        --('CA' || L_CASE_ID),
--        L_CASE_ID,
--        '9'
--        );
--        INSERT INTO HJ_LOGS (DESCRIPTION,V_USER_CASES) VALUES (('CA' || L_CASE_ID),'else');

        IF PRODUCT = 'LC'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            EXPIRY_PLACE,
                            CNTRCT_AMT,
                            SHPMT_FRM,
                            SHPMT_TO,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            SHPMT_DTLS,
                            ADDL_CONDITION_TX,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            INSTR_TX,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         (L_CASE_ID),
                         L_CONTRACT_REC.LCID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         'AMND',
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'LC',
                         'ILC',
                         L_CONTRACT_REC.EXPIRYPLACE,
                         L_CONTRACT_REC.LCAMOUNT,
                         L_CONTRACT_REC.SHIPDESPATCH,
                         L_CONTRACT_REC.FINALDESTINATION,
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.OTHERDESPATCHDET,
                         L_CONTRACT_REC.ADDITIONALCOND,
                         L_CONTRACT_REC.LCID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.COMMODITY,
                         L_CONTRACT_REC.INSTRUCTIONS,
                         L_CONTRACT_REC.LCAMOUNT);
        ELSIF PRODUCT = 'MD'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         (L_CASE_ID),
                         L_CONTRACT_REC.MDID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         'AMND',
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'MD',
                         'MD',
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.MDID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.GOODSDESCRIPTION);
        ELSIF PRODUCT = 'WB'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            CNTRCT_AMT,
                            DISCHRG_PORT,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            GOOD_SRVC_CT,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         (L_CASE_ID),
                         L_CONTRACT_REC.WBID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         'AMND',
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'WB',
                         'WB',
                         L_CONTRACT_REC.TOTEFORMAMT,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.WBID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.COMMODITY,
                         L_CONTRACT_REC.HSQUANT,
                         L_CONTRACT_REC.TOTEFORMAMT);
        ELSIF PRODUCT = 'DR'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            CNTRCT_AMT,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            SHPMT_TO,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            GOOD_SRVC_CT,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         (L_CASE_ID),
                         L_CONTRACT_REC.DRID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         'AMND',
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'DR',
                         'DR',
                         L_CONTRACT_REC.DOCAMNT,
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.FINALDESTINATION,
                         L_CONTRACT_REC.DRID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.GOODSDESCRIPTION,
                         L_CONTRACT_REC.HSQUANT,
                         L_CONTRACT_REC.DOCAMNT);
        ELSIF PRODUCT = 'FT'
        THEN
            INSERT INTO KDD_CASE_TRD_FIN_CNTRCT_EVT (
                            CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID,
                            CASE_INTRL_ID,
                            TRD_FIN_CNTRCT_INTRL_ID,
                            CNTRCT_EVENT_NB,
                            CNTRCT_EVENT_DT,
                            CNTRCT_EVENT_TYPE_CD,
                            CNTRCT_EVENT_TM,
                            CNTRCT_EVENT_UTC_DT,
                            CNTRCT_EVENT_UTC_TM,
                            IRREVOCABLE_FL,
                            TFRABLE_FL,
                            STANDBY_FL,
                            REVOLVING_FL,
                            PART_SHPMT_FL,
                            TRANS_SHPMT_FL,
                            PRDCT_TYPE_CD,
                            PRDCT_SUB_TYPE_CD,
                            CNTRCT_AMT,
                            SHPMT_TO,
                            LOAD_PORT,
                            DISCHRG_PORT,
                            PARTY_CNTRCT_INTRL_ID,
                            SRC_SYS_CD,
                            PRCSNG_BATCH_NM,
                            DATA_DUMP_DT,
                            GOOD_SRVC_DESC_TX,
                            GOOD_SRVC_CT,
                            CNTRCT_ACTVY_AMT)
                 VALUES ((L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID),
                         (L_CASE_ID),
                         L_CONTRACT_REC.FTID,
                         L_CONTRACT_REC.VERSION,
                         CURR_DATE,
                         'AMND',
                         '999999',
                         CURR_DATE,
                         '999999',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'N',
                         'FT',
                         'FT',
                         L_CONTRACT_REC.CREDITAMNT,
                         L_CONTRACT_REC.FINALDESTINATION,
                         L_CONTRACT_REC.PORTOFLOADING,
                         L_CONTRACT_REC.PORTOFDISCHARGE,
                         L_CONTRACT_REC.FTID,
                         P_DATA_ORIGIN,
                         P_DATA_ORIGIN,
                         CURR_DATE,
                         L_CONTRACT_REC.GOODSDESCRIPTION,
                         L_CONTRACT_REC.HSQUANT,
                         L_CONTRACT_REC.CREDITAMNT);
        END IF;

        SELECT *
          INTO L_EVENT_REC
          FROM KDD_CASE_TRD_FIN_CNTRCT_EVT
         WHERE CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID =
               L_CASE_TRD_FIN_CNTRCT_EVT_SEQ_ID;

        SELECT CM_TRD_FIN_CNTRCT_SEQ.NEXTVAL
          INTO L_CASE_TRD_FIN_CNTRCT_SEQ_ID
          FROM DUAL;

        DELETE FROM KDD_CASE_TRD_FIN_CNTRCT
              WHERE CASE_INTRL_ID = L_CASE_ID;

        INSERT INTO KDD_CASE_TRD_FIN_CNTRCT (CASE_TRD_FIN_CNTRCT_SEQ_ID,
                                             CASE_INTRL_ID,
                                             TRD_FIN_CNTRCT_INTRL_ID,
                                             LAST_EVENT_TYPE_CD,
                                             LAST_UPDATE_DT,
                                             LAST_UPDATE_TM,
                                             LAST_UPDATE_UTC_TM,
                                             LAST_UPDATE_UTC_DT,
                                             IRREVOCABLE_FL,
                                             LAST_TFRABLE_FL,
                                             STANDBY_FL,
                                             REVOLVING_FL,
                                             PART_SHPMT_FL,
                                             TRANS_SHPMT_FL,
                                             TOT_UNCNFRMND_AMD_CT,
                                             TOT_CNFRMND_AMD_CT,
                                             TOT_REJ_AMD_CT,
                                             AMD_EVENT_CT,
                                             PRDCT_TYPE_CD,
                                             PRDCT_SUB_TYPE_CD,
                                             LAST_EXPIRY_PLACE,
                                             LAST_CNTRCT_AMT,
                                             LAST_SHPMT_FRM,
                                             LAST_SHPMT_TO,
                                             LAST_LOAD_PORT,
                                             LAST_DISCHRG_PORT,
                                             LAST_SHPMT_DTLS,
                                             LAST_CNTRCT_ACTVY_AMT,
                                             LAST_SHPT_CNTRY_CD,
                                             LAST_SHPF_CNTRY_CD,
                                             LAST_ADDL_CONDITION_TX,
                                             LAST_INSTR_TX,
                                             LAST_PARTY_CNTRCT_INTRL_ID,
                                             SRC_SYS_CD,
                                             LAST_PRCSNG_BATCH_NM,
                                             LAST_DATA_DUMP_DT,
                                             LAST_GOOD_SRVC_DESC_TX)
             VALUES ((L_CASE_TRD_FIN_CNTRCT_SEQ_ID),
                     L_EVENT_REC.CASE_INTRL_ID,
                     L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                     L_EVENT_REC.CNTRCT_EVENT_TYPE_CD,
                     L_EVENT_REC.CNTRCT_EVENT_DT,
                     L_EVENT_REC.CNTRCT_EVENT_TM,
                     L_EVENT_REC.CNTRCT_EVENT_UTC_TM,
                     L_EVENT_REC.CNTRCT_EVENT_UTC_DT,
                     'N',
                     'N',
                     'N',
                     'N',
                     'N',
                     'N',
                     0,
                     0,
                     0,
                     0,
                     L_EVENT_REC.PRDCT_TYPE_CD,
                     L_EVENT_REC.PRDCT_SUB_TYPE_CD,
                     L_EVENT_REC.EXPIRY_PLACE,
                     L_EVENT_REC.CNTRCT_AMT,
                     L_EVENT_REC.SHPMT_FRM,
                     L_EVENT_REC.SHPMT_TO,
                     L_EVENT_REC.LOAD_PORT,
                     L_EVENT_REC.DISCHRG_PORT,
                     L_EVENT_REC.SHPMT_DTLS,
                     L_EVENT_REC.CNTRCT_ACTVY_AMT,
                     L_EVENT_REC.SHPT_CNTRY_CD,
                     L_EVENT_REC.SHPF_CNTRY_CD,
                     L_EVENT_REC.ADDL_CONDITION_TX,
                     L_EVENT_REC.INSTR_TX,
                     L_EVENT_REC.PARTY_CNTRCT_INTRL_ID,
                     L_EVENT_REC.SRC_SYS_CD,
                     L_EVENT_REC.PRCSNG_BATCH_NM,
                     L_EVENT_REC.DATA_DUMP_DT,
                     L_EVENT_REC.GOOD_SRVC_DESC_TX);

        SELECT CM_TRD_FIN_GOOD_SRVC_SEQ.NEXTVAL
          INTO L_CASE_TRD_FIN_GOOD_SRVC_SEQ_ID
          FROM DUAL;

        DELETE FROM KDD_CASE_TRD_FIN_GOOD_SRVC
              WHERE CASE_INTRL_ID = L_CASE_ID;

        INSERT INTO KDD_CASE_TRD_FIN_GOOD_SRVC (
                        CASE_TRD_FIN_GOOD_SRVC_SEQ_ID,
                        CASE_INTRL_ID,
                        TRD_FIN_CNTRCT_INTRL_ID,
                        EVENT_NB,
                        GOOD_SRVC_DESC_TX,
                        GOOD_SRVC_CD,
                        UNIT_PR,
                        UNIT_PR_CRNCY_CD,
                        UNIT_QT,
                        SRC_SYS_CD,
                        PRCSNG_BATCH_NM,
                        DATA_DUMP_DT)
             VALUES ((L_CASE_TRD_FIN_GOOD_SRVC_SEQ_ID),
                     L_EVENT_REC.CASE_INTRL_ID,
                     L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                     L_EVENT_REC.CNTRCT_EVENT_NB,
                     L_EVENT_REC.GOOD_SRVC_DESC_TX,
                     L_CONTRACT_REC.HSCODE,
                     L_CONTRACT_REC.SELLUNITPRICE,
                     L_CONTRACT_REC.UNITCURRENCY,
                     L_EVENT_REC.GOOD_SRVC_CT,
                     P_DATA_ORIGIN,
                     P_DATA_ORIGIN,
                     CURR_DATE);

        SELECT CM_TRD_FIN_PARTY_SEQ.NEXTVAL
          INTO L_CASE_TRD_FIN_PARTY_SEQ_ID
          FROM DUAL;

        SELECT *
          INTO L_BENE_REC
          FROM BENEFICIARY_DETAIL_STAGING BD
         WHERE BD.MESSAGE_ID = (SELECT MESSAGE_ID FROM RT_CASE_TRACKING);

        DELETE FROM KDD_CASE_TRD_FIN_PARTY
              WHERE CASE_INTRL_ID = L_CASE_ID;

        IF PRODUCT = 'LC'
        THEN
            IF L_BENE_REC.BENEFICIARY IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'BENE',
                             L_BENE_REC.BENEFICIARY,
                             L_BENE_REC.BENEFICIARY,
                             'N',
                             L_BENE_REC.BENEFICIARYCUSTNO,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;

            IF L_BENE_REC.ADVISETHRU IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'ADVS',
                             L_BENE_REC.ADVISETHRU,
                             L_BENE_REC.ADVISETHRU,
                             'N',
                             L_BENE_REC.ADVISETHRUCUSTNO,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;

            IF L_BENE_REC.ADVISINGBK IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'RCV',
                             L_BENE_REC.ADVISINGBK,
                             L_BENE_REC.ADVISINGBK,
                             'N',
                             L_BENE_REC.ADVISINGBKCUSTNO,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        ELSIF PRODUCT = 'DR'
        THEN
            IF L_BENE_REC.SENDERCORR IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'SEND',
                             L_BENE_REC.SENDERCORR,
                             L_BENE_REC.SENDERCORR,
                             'N',
                             L_BENE_REC.SENDERCORR,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            ELSIF L_BENE_REC.RECEIVERSBANK IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'RCV',
                             L_BENE_REC.RECEIVERSBANK,
                             L_BENE_REC.RECEIVERSBANK,
                             'N',
                             L_BENE_REC.RECEIVERSCORR,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            ELSIF L_BENE_REC.INTERMEDBANK IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'GUAR',
                             L_BENE_REC.INTERMEDBANK,
                             L_BENE_REC.INTERMEDBANK,
                             'N',
                             L_BENE_REC.INTERMEDBANK,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        --ELSIF PRODUCCT = 'WB'     NEED TO UNDERSTAND MAPPING
        ELSIF PRODUCT = 'MD'
        THEN
            IF L_BENE_REC.BENEFICIARY IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'BENE',
                             L_BENE_REC.BENEFCUST1,
                             L_BENE_REC.BENEFCUST1,
                             'N',
                             L_BENE_REC.BENEFCUST1,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        ELSIF PRODUCT = 'FT'
        THEN
            IF L_BENE_REC.BENEFICIARYCUST IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_TRD_FIN_PARTY (
                                CASE_TRD_FIN_PARTY_SEQ_ID,
                                CASE_INTRL_ID,
                                TRD_FIN_CNTRCT_INTRL_ID,
                                EVENT_NB,
                                PARTY_ROLE_CD,
                                PARTY_NM,
                                PARTY_AUG_NM,
                                UNKWN_PARTY_NM_FL,
                                PARTY_ID,
                                PARTY_ID_TYPE_CD,
                                ADDR_PURP_CD,
                                SRC_SYS_CD,
                                PARTY_NTITY_RISK_NB,
                                PARTY_ACTVY_RISK_NB,
                                PRCSNG_BATCH_NM,
                                DATA_DUMP_DT)
                     VALUES (CM_TRD_FIN_PARTY_SEQ.NEXTVAL,
                             L_EVENT_REC.CASE_INTRL_ID,
                             L_EVENT_REC.TRD_FIN_CNTRCT_INTRL_ID,
                             L_EVENT_REC.CNTRCT_EVENT_NB,
                             'BENE',
                             L_BENE_REC.BENEFICIARYCUST,
                             L_BENE_REC.BENEFICIARYCUST,
                             'N',
                             L_BENE_REC.BENEFICIARYID,
                             'OT',
                             'B',
                             P_DATA_ORIGIN,
                             5,
                             5,
                             P_DATA_ORIGIN,
                             CURR_DATE);
            END IF;
        END IF;

        L_N_RUN_SKEY_B := L_N_RUN_SKEY;
    L_N_EVENT_SKEY_B := L_N_EVENT_SKEY;
    L_N_EVENTS_ENTITY_MAP_SEQ_B := L_N_EVENTS_ENTITY_MAP_SEQ;

 FOR EVENT_RECORD IN (SELECT MSG_ID,INPUT_NAME,INPUT_MATCH,DESCRIPTION FROM EVENT_STAGING WHERE MSG_ID = MESSAGE_ID)
    LOOP
        ----POPULATING EVENT TABLE-----
        INSERT INTO FCC_EVENTS (N_EVENT_SKEY,
                                N_RUN_SKEY,
                                D_MIS_DATE,
                                V_EVENT_CD,
                                V_EVENT_TYPE,
                                V_DATA_ORIGIN,
                                V_STATUS_CD,
                                D_EVENT_CREATED_DATE,
                                N_EVENT_SCORE,
                                V_JURISDICTION_CD,
                                V_BUSINESS_DOMAIN_CD,
                                V_EVENT_PRIORITY,
                                V_COMMENTS,
                                V_SCENARIO_CLASS)
             VALUES ((L_N_EVENT_SKEY_B),
                     (L_N_RUN_SKEY),
                     CURR_DATE,
                     ('C' || L_N_EVENT_SKEY_B),
                     P_EVENT_TYPE,
                     P_DATA_ORIGIN,
                     P_EVENT_STATUS,
                     CURR_DATE,
                     0,
                     P_JRSDCN_CD,
                     P_BUS_DMN,
                     P_EVENT_PRIORITY,
                     'COMMENT',
                     P_SCENARIO_CLASS);

        -----POPULATING fcc_event_entity_map-------
        INSERT INTO fcc_event_entity_map (N_RUN_SKEY,
                                          N_EVENT_SKEY,
                                          N_ENTITY_SKEY,
                                          V_ENTITY_TYPE,
                                          V_FOCUS_FLAG,
                                          V_DIRECT_ENTITY_FLAG,
                                          V_ENTITY_NAME,
                                          N_EVENTS_ENTITY_MAP_SEQ,
                                          V_ENTITY_CD)
             VALUES ((L_N_RUN_SKEY),
                     (L_N_EVENT_SKEY_B),
                     (L_N_ENTITY_SKEY),
                     EVENT_RECORD.INPUT_NAME,
                     'Y',
                     'Y',
                     'ENTITY_NAME',
                     ---NEEDS TO BE UPDATED FROM LC
                     (L_N_EVENTS_ENTITY_MAP_SEQ),
                     EVENT_RECORD.INPUT_MATCH);

        ----Populating fcc_event_details-----
        INSERT INTO fcc_event_details (N_RUN_SKEY,
                                       N_EVENT_SKEY,
                                       V_EVENT_REASON,
                                       N_SCENARIO_SKEY)
             VALUES ((L_N_RUN_SKEY),
                     (L_N_EVENT_SKEY_B),
                     EVENT_RECORD.DESCRIPTION,
                     2);

        ----POPULATING FCC_CORRELATION_EVENT_MAP -----
        INSERT INTO FCC_CORRELATION_EVENT_MAP (N_CORR_EVENT_MAP_SKEY,
                                               N_EVENT_CORRELATION_SKEY,
                                               N_EVENT_SKEY,
                                               D_LINK_CREATION_DATE,
                                               N_EVENT_PRIORITY,
                                               N_RUN_SKEY)
             VALUES ((L_N_EVENT_CORRELATION_SKEY),
                     (L_N_EVENT_CORR_SKEY),
                     (L_N_EVENT_SKEY_B),
                     CURR_DATE,
                     1,
                     (L_N_RUN_SKEY));


        -- This will bypass the PRODUCTIVE/NON-PRODUCTIVE 
         INSERT INTO FCC_EVENT_INVESTIGATION_STATUS (
        N_EVENT_SKEY,
        N_STATUS_ID,
        D_ACTION_DATE,
        N_ACTION_BY) VALUES (
        L_N_EVENT_SKEY_B, 
        1, 
        sysdate, 
        1);

        SELECT   CM_EVENTS_ENTITY_MAP_SEQ.NEXTVAL
        INTO   L_N_EVENTS_ENTITY_MAP_SEQ_B
        FROM   DUAL;

        SELECT   CM_EVENT_LOOKUP_SEQ.NEXTVAL INTO L_N_EVENT_SKEY_B FROM DUAL;

        SELECT   CM_BATCH_RUN_SEQ.NEXTVAL INTO L_N_RUN_SKEY_B FROM DUAL;

END LOOP;
        ---POPULATING FCC_PRECASE_CASE_MAP------
        INSERT INTO FCC_PRECASE_CASE_MAP (N_EVENT_CORRELATION_SKEY,
                                          V_CASE_ID,
                                          V_CASE_TYPE,
                                          N_RUN_SKEY,
                                          V_JURISDICTION_CD,
                                          V_BUSINESS_DOMAIN_CD,
                                          V_CORRELATION_TYPE)
             VALUES ((L_N_EVENT_CORR_SKEY),
                     (L_CASE_ID),
                     P_CASE_TYPE,
                     (L_N_RUN_SKEY),
                     P_JRSDCN_CD,
                     P_BUS_DMN,
                     NULL);



        ---------populating kdd_cases table-------------
        DELETE FROM KDD_CASES
              WHERE CASE_INTRL_ID = L_CASE_ID;

        INSERT INTO KDD_CASES (CASE_INTRL_ID,
                               CASE_REVIEW_ID,
                               CASE_TITL_NM,
                               CREATED_TS,
                               LAST_UPDATED_BY_ID,
                               CREATE_BY_ID,
                               OWNER_ID,
                               ASSIGNED_TO_ID,
                               ASSIGNED_BY_ID,
                               CONFIDENTIAL_FL,
                               CASE_ASSIGNED_TS,
                               CASE_DESC_TX,
                               STATUS_CD,
                               LAST_ACTION_NAME,
                               ORIG_OWNER_ID,
                               OWNER_ORG,
                               JRSDCN_CD,
                               CASE_SRC_CD,
                               PRIORITY_CD,
                               BUS_DMN_ST,
                               SCORE_CT,
                               PRCSNG_CMPLT_FL,
                               CASE_TYPE_SUBTYPE_CD,
                               CASE_TYPE_CD,
                               AGE,
                               PURGE_RULE_OVERRIDE_FL)
             VALUES (('CA'||L_CASE_ID),
                     (L_CASE_ID),
                     NVL(ACC_TITLE,'N/A'),
                     CURR_DATE,
                     1,
                     1,
                     P_OWNER_ID,
                     P_OWNER_ID,
                     1,
                     'Y',
                     CURR_DATE,
                     'REAL-TIME-CASE',
                     P_CASE_STATUS,
                     'Create Case',
                     P_OWNER_ID,
                     P_OWNER_ORG,
                     P_JRSDCN_CD,
                     P_CASE_SRC,
                     P_CASE_PRIORITY,
                     'c',
                     0,
                     'Y',
                     P_CASE_TYPE,
                     P_CASE_TYPE,
                     0,
                     'Y');

        SELECT (L_CASE_ID) INTO RESID FROM DUAL;


        -------POPULATING CUSTOMER TABLE------------
        DELETE FROM KDD_CASE_CUSTOMERS
              WHERE CASE_INTRL_ID = L_CASE_ID;

        SELECT *
          INTO L_CUSTOMER_REC
          FROM CUST_MASTER_STAGING
         WHERE MESSAGE_ID = (SELECT MESSAGE_ID FROM RT_CASE_TRACKING);
        BEGIN
    SELECT NVL(RISK,'N/A') INTO CUST_RISK FROM CUST_RISK_MAP_CSTM WHERE CUST_INTRL_ID = (
                SELECT NVL(COALESCE( L_CUSTOMER_REC.APPLICANTCUSTNO, L_CUSTOMER_REC.DRAWEECUSTNO, L_CUSTOMER_REC.PRESENTORCUST, L_CUSTOMER_REC.ORDERINGCUST, L_CUSTOMER_REC.CUSTOMER, L_CUSTOMER_REC.CUSTOMERID),'N/A') FROM DUAL);

              EXCEPTION WHEN OTHERS THEN
              CUST_RISK := 'N/A';
             END;
        IF PRODUCT = 'LC'
        THEN
            IF L_CUSTOMER_REC.APPLICANTCUSTNO IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.APPLICANTCUSTNO,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.APPLICANT);
            END IF;

            IF L_CUSTOMER_REC.DRAWEECUSTNO IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.DRAWEECUSTNO,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.DRAWEE);
            END IF;
        ELSIF PRODUCT = 'DR'
        THEN
            IF L_CUSTOMER_REC.PRESENTORCUST IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.PRESENTORCUST,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.PRESENTOR);
            END IF;
        ELSIF PRODUCT = 'FT'
        THEN
            IF L_CUSTOMER_REC.ORDERINGCUST IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.ORDERINGCUST,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.ORDERINGBANK);
            END IF;

            IF L_CUSTOMER_REC.CUSTOMER IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.CUSTOMER,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.CUSTOMER);
            END IF;
        ELSIF PRODUCT = 'MD'
        THEN
            IF L_CUSTOMER_REC.CUSTOMER IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.CUSTOMER,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.CUSTOMER);
            END IF;
        ELSIF PRODUCT = 'WB'
        THEN
            IF L_CUSTOMER_REC.CUSTOMERID IS NOT NULL
            THEN
                INSERT INTO KDD_CASE_CUSTOMERS (CUST_INTRL_ID,
                                                CUST_TYPE_CD,
                                                CUST_EFCTV_RISK_NB,
                                                JRSDCN_CD,
                                                CUST_STAT_CD,
                                                CASE_CUST_SEQ_ID, CUST_CDT_RTNG,
                                                CASE_INTRL_ID,
                                                BUS_DMN_LIST_TX,
                                                FULL_NM)
                     VALUES (L_CUSTOMER_REC.CUSTOMERID,
                             'IND',
                             5,
                             P_JRSDCN_CD,
                             'A',
                             CM_CUST_SEQ.NEXTVAL, CUST_RISK,
                             (L_CASE_ID),
                             'c',
                             L_CUSTOMER_REC.CUSTOMERID);
            END IF;
        END IF;
    END IF;


    EXECUTE IMMEDIATE 'TRUNCATE TABLE RT_CASE_TRACKING';
    --VBO_RETURN := FN_CMINHERITANCE(('CA' || L_CASE_ID),'10008','') ;
        --inserting newcase action
        INSERT INTO KDD_CASE_ACTIONS (
        ACTION_SEQ_ID,
        ACTION_BY_ID,
        ACTION_TS,
        STATUS_CD,
        CASE_INTRL_ID,
        ACTION_ID
        )
        VALUES (
        cm_case_action_seq.nextval,
        'P_OWNER_ID',
        CURR_DATE,
        P_CASE_STATUS,
        --'',
        ('CA' || L_CASE_ID),
        --L_CASE_ID,
        '9'
        --'1005' --intiate workflow_actionno
        );

   COMMIT;
    --INSERT INTO HJ_LOGS (DESCRIPTION) VALUES (VBO_RETURN);
    ---------mapping case_action ---------

--        
        -- Resolution for P/NP case issue
--INSERT INTO FCC_EVENT_INVESTIGATION_STATUS(
--N_EVENT_SKEY,
--N_STATUS_ID,
----D_ACTION_DATE,
--N_ACTION_BY) VALUES (
--L_CASE_ID,
--1,
--10008
--);
--        
-- 
--SELECT FN_CMVBO(('CA' || L_N_EVENT_SKEY),'ECMADMIN', 'DetailView') INTO VBO_RETURN FROM DUAL;
-- VBO must execute before this line

--SELECT sysdate into CURR_DATE from dual;
--
--         INSERT INTO KDD_CASE_ACTIONS (
--        ACTION_SEQ_ID,
--        ACTION_BY_ID,
--        ACTION_TS,
--        STATUS_CD,
--        CASE_INTRL_ID,
--        ACTION_ID,
--        NEW_CASE_OWNR_ASSGN_ID
--        )
--        VALUES (
--        cm_case_action_seq.nextval,
--        '10008',
--        CURR_DATE,
--        'INV',
--        ('CA' || L_N_EVENT_SKEY),
--        --L_CASE_ID,
--        '208', --VBO action
--        10008 
--);
--
--
--    UPDATE KDD_CASES SET 
--    LAST_UPDATED_TS=CURR_DATE,
--    LAST_UPDATED_BY_ID=10008,
--    CASE_ASSIGNED_TS=CURR_DATE,
--    STATUS_CD = 'INV',
--    LAST_ACTION_NAME = 'Viewed by Owner'
--    WHERE (CASE_INTRL_ID =('CA' || L_N_EVENT_SKEY)); 
    --commit;

        -- inserting VBO action
--        INSERT INTO KDD_CASE_ACTIONS (
--        ACTION_SEQ_ID,
--        ACTION_BY_ID,
--        ACTION_TS,
--        STATUS_CD,
--        CASE_INTRL_ID,
--        ACTION_ID
--        )
--        VALUES (
--        cm_case_action_seq.nextval,
--        '10008',
--        sysdate,
--        'INV',
--        --'',
--        ('CA' || L_N_EVENT_SKEY),
--        --L_CASE_ID,
--        '9'
--        --'1005' --intiate workflow_actionno
--        );
--       
--        
--         INSERT INTO KDD_CASE_ACTIONS (
--        ACTION_SEQ_ID,
--        ACTION_BY_ID,
--        ACTION_TS,
--        STATUS_CD,
--        CASE_INTRL_ID,
--        ACTION_ID
--        )
--        VALUES (
--        cm_case_action_seq.nextval,
--        '10008',
--        sysdate,
--        'INV',
--        ('CA' || L_N_EVENT_SKEY),
--        --L_CASE_ID,
--        '823'
--        );

       -- UPDATE KDD_CASES SET STATUS_CD = 'INV' WHERE (CASE_INTRL_ID =('CA' || L_N_EVENT_SKEY));

        --INSERT INTO HJ_LOGS (DESCRIPTION) VALUES ('CAss' || L_N_EVENT_SKEY);
--        EXCEPTION
--        WHEN OTHERS THEN
--        RAISE;
--        INSERT INTO MYLOGS(TXT) VALUES ('Error occured : '|| DBMS_UTILITY.FORMAT_ERROR_BACKTRACE);
--        RESID :=  'Error occured : '|| DBMS_UTILITY.FORMAT_ERROR_BACKTRACE
--        RAISE_APPLICATION_ERROR(-20001,'Error occured : '|| DBMS_UTILITY.FORMAT_ERROR_BACKTRACE, true);

END;