namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.Cstm_Brains — the FCCM (TBML) vs BRAINS risk-score
/// comparison record, ported from the legacy caseInsertionController pipeline.
///
/// Legacy semantics (preserved verbatim):
///   • <see cref="FccmScore"/>   — numeric FCCM/TBML band (1 Low / 2 Medium / 3 High)
///                                 derived from FCC_OB_RA.RISK_CATEGORY_KEY.
///   • <see cref="BrainsScore"/> — numeric BRAINS band (0 PEP / 1 High / 2 Medium /
///                                 3 Low per the servlet dictionary).
///   • <see cref="FinalScore"/>  — the "higher-risk wins" outcome label
///                                 (Low / Medium / High) after reconciling the two.
///   • <see cref="Display"/>     — the label plus its attribution suffix
///                                 (" - TBML" / " - BRAINS" / " - BRAINS &amp; TBML
///                                 SCORES ARE EQUAL"), i.e. exactly what the legacy
///                                 code wrote back into UDF_Data.$.RiskScore.
///
/// One row per transaction (unique on <see cref="TransactionId"/>); the poller
/// upserts it when FCCM risk assessment completes.
/// </summary>
public class CstmBrains
{
    public long      Id             { get; set; }
    public long      TransactionId  { get; set; }
    public string?   CustomerNo     { get; set; }
    public int?      FccmScore      { get; set; }
    public int?      BrainsScore    { get; set; }
    public string?   FinalScore     { get; set; }
    public string?   Display        { get; set; }
    public DateTime  CreatedAt      { get; set; }
    public DateTime  LastUpdatedAt  { get; set; }
}
