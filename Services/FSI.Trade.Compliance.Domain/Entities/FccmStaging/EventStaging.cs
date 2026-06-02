namespace FSI.Trade.Compliance.Domain.Entities.FccmStaging;

/// <summary>
/// Plain POCO for the legacy <c>EVENT_STAGING</c> table on the FCCM
/// Oracle DB (schema <c>MANTAS</c>). One row per "event" —
/// customer-details events + manual-red-flag events. The Oracle SP
/// <c>PR_CREATE_EVENT_DEMO</c> reads these rows (filtered by
/// <see cref="MsgId"/>) and turns each one into an observation on
/// the FCCM case it creates.
///
/// NOT EF-mapped — Slice 9 went Option A (pure ADO.NET, single
/// <c>Oracle.ManagedDataAccess.Core</c> dep). Column-name mappings
/// (UPPER_SNAKE_CASE) live inside the parameterised INSERT in
/// <c>FccmOracleReader.InsertEventStagingAsync</c>. See
/// <c>docs/LEGACY_FCCM_TRADE_FLOW.md</c> for the full coverage rationale.
/// </summary>
public class EventStaging
{
    public int       Id          { get; set; }
    public string?   MsgId       { get; set; }
    public string?   InputName   { get; set; }
    public string?   InputMatch  { get; set; }
    public string?   Description { get; set; }
    public DateTime? TsDate      { get; set; }
    public string?   Title       { get; set; }
}
