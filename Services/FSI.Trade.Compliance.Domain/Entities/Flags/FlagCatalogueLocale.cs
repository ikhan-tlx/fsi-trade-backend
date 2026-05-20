namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// Per-locale translation for a <see cref="FlagCatalogue"/> entry.
/// Maps to <c>dbo.TmX_Flag_Catalogue_Locale</c>. The base catalogue
/// row carries the canonical (typically English) text; this table
/// holds the rest.
///
/// One row per (FlagId, LocaleId). The migration seed leaves this
/// table empty by design — translations get authored by admins when
/// multi-locale deployment is needed.
/// </summary>
public class FlagCatalogueLocale
{
    public int       FlagCatalogueLocaleId { get; set; }
    public int       FlagId                { get; set; }
    public int       LocaleId              { get; set; }
    public string?   LocaleName            { get; set; }
    public string?   LocaleDescription     { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}
