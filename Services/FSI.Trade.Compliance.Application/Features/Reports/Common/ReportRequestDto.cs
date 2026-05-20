namespace FSI.Trade.Compliance.Application.Features.Reports.Common;

/// <summary>
/// Shared request shape for the three report endpoints — kept loose to
/// match the legacy AngularJS <c>ReportService</c> payload, which sends
/// the same body to all three endpoints (the verb tells the dispatcher
/// which one runs).
///
/// <see cref="Arguments"/> is the SP parameter map; keys are parameter
/// names (the '@' prefix is optional and added by the SP runner).
/// </summary>
public class ReportRequestDto
{
    /// <summary>
    /// Logical report name. Used to look up the matching
    /// <c>TmX_Template</c> row whose Liquid body renders the HTML.
    /// </summary>
    public string?  ReportName        { get; set; }

    /// <summary>
    /// Human-readable name (sheet name, PDF download filename). Carried
    /// through end-to-end so the user-visible artefacts match what's on
    /// screen.
    /// </summary>
    public string?  ReportVisibleName { get; set; }

    /// <summary>
    /// SP name to execute. Validated against the REPORT_TYPE lookup
    /// allowlist before being passed to <c>IStoredProcedureRunner</c>.
    /// </summary>
    public string?  StoredProcedure   { get; set; }

    /// <summary>
    /// Free-form SP parameter map. Nulls are sent as DBNull. Empty
    /// dictionary is fine — SPs without parameters work too.
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>
    /// Pre-rendered HTML body — only the PDF endpoint reads this. The
    /// FE generates HTML via <c>POST /Report/ReportHTML</c>, then sends
    /// it back here for PDF conversion.
    /// </summary>
    public string?  HTML              { get; set; }

    /// <summary>
    /// "L"/"Landscape" → landscape, anything else → portrait. Mirrors
    /// the legacy convention. Only the PDF endpoint cares.
    /// </summary>
    public string?  PageOrientation   { get; set; }
}
