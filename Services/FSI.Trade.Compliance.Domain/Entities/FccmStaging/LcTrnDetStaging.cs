namespace FSI.Trade.Compliance.Domain.Entities.FccmStaging;

/// <summary>
/// Plain POCO for the legacy <c>LC_TRN_DET_STAGING</c> table on the
/// FCCM Oracle DB (schema <c>MANTAS</c>) — TRADE FIELDS subset.
/// The legacy table has ~330 columns of which ~250 are per-(subtype,
/// slot) manual-red-flag duplications (Iifmrl1..16, Eapmrl1..16, etc.).
/// Those are dropped here per the Slice 8 catalogue model — flag events
/// flow through <see cref="EventStaging"/> using
/// <c>FlagCatalogue.Flag_Description</c> as the single source of truth.
///
/// Un-modeled columns stay in the Oracle table; the INSERT in
/// <c>FccmOracleReader.InsertLcTrnDetStagingAsync</c> just doesn't
/// include them, letting Oracle apply default/NULL.
///
/// LEGACY NAMING QUIRK PRESERVED:
///   • <c>Prouct</c> — typo on the legacy column name. Matched verbatim
///     so the INSERT statement column list lines up with the actual DB.
///
/// NOT EF-mapped — see <c>EventStaging.cs</c> header for the rationale.
/// </summary>
public class LcTrnDetStaging
{
    public string?   MessageId         { get; set; }

    /// <summary>Legacy spelling — "Prouct" (typo). Stores product code (e.g. "ILF").</summary>
    public string?   Prouct            { get; set; }

    // ---- Identifiers ----
    public string?   Ftid              { get; set; }
    public string?   Drid              { get; set; }
    public string?   Wbid              { get; set; }
    public string?   Mdid              { get; set; }
    public string?   Lcid              { get; set; }

    // ---- HS / commodity ----
    public string?   Fthscode          { get; set; }
    public string?   Hscode            { get; set; }
    public string?   Hscodeofgoods     { get; set; }
    public string?   Hsunit            { get; set; }
    public decimal?  Hsquant           { get; set; }
    public string?   Commodity         { get; set; }
    public decimal?  Quantity          { get; set; }
    public string?   Unitcurrency      { get; set; }

    // ---- Amounts / pricing ----
    public decimal?  Lcamount          { get; set; }
    public decimal?  Debitamount       { get; set; }
    public decimal?  Creditamnt        { get; set; }
    public decimal?  Toteformamt       { get; set; }
    public decimal?  Docamnt           { get; set; }
    public decimal?  Eifamnt           { get; set; }
    public decimal?  Sellunitprice     { get; set; }
    public decimal?  Flrunitprice      { get; set; }
    public decimal?  Capunitprice      { get; set; }
    public decimal?  Version           { get; set; }

    // ---- Geography ----
    public string?   Country           { get; set; }
    public string?   Portofloading     { get; set; }
    public string?   Portofdischarge   { get; set; }
    public string?   Sbpshipcity       { get; set; }
    public string?   Finaldest         { get; set; }
    public string?   Finaldestination  { get; set; }

    // ---- Goods / shipping details ----
    public string?   Descgoods         { get; set; }
    public string?   Goodsdescription  { get; set; }
    public string?   Shipdespatch      { get; set; }
    public string?   Transportation    { get; set; }
    public string?   Otherdespatchdet  { get; set; }

    // ---- LC details ----
    public string?   Expiryplace       { get; set; }
    public string?   Additionalcond    { get; set; }
    public string?   Instructions      { get; set; }
    public string?   Lremarks          { get; set; }
}
