namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to the EXISTING legacy ICBC_DEMO.dbo.Cstm_Brains table — the FCCM (TBML)
/// vs BRAINS risk-score comparison record, ported from the legacy
/// caseInsertionController pipeline. Column shape is preserved verbatim from the
/// legacy schema (all score columns are varchar, no surrogate key):
///
///   Brains_Score varchar(100), Final_Score varchar(100), FCCM_Score varchar(100),
///   Transaction_ID int, Display varchar(100), Customer_No varchar(1000)
///
/// Legacy semantics:
///   • <see cref="FccmScore"/>   — numeric FCCM/TBML band as text (1 Low / 2 Medium
///                                 / 3 High) from FCC_OB_RA.RISK_CATEGORY_KEY.
///   • <see cref="BrainsScore"/> — numeric BRAINS band as text (0 PEP / 1 High /
///                                 2 Medium / 3 Low per the servlet dictionary).
///   • <see cref="FinalScore"/>  — the reconciled "higher-risk wins" numeric result
///                                 as text (0/1/2/3).
///   • <see cref="Display"/>     — the label plus attribution suffix (e.g.
///                                 "High - TBML"), i.e. exactly what the legacy code
///                                 wrote into UDF_Data.$.RiskScore.
///
/// <see cref="TransactionId"/> is the natural key (one row per transaction); the
/// poller upserts on it.
/// </summary>
public class CstmBrains
{
    public int      TransactionId  { get; set; }   // Transaction_ID (key)
    public string?  CustomerNo     { get; set; }
    public string?  FccmScore      { get; set; }
    public string?  BrainsScore    { get; set; }
    public string?  FinalScore     { get; set; }
    public string?  Display        { get; set; }
}
