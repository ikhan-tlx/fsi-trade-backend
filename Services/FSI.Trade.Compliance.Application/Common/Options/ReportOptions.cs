namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Slice 7 — Reports stack runtime tuning. Bound from the <c>Reports</c>
/// section in appsettings.json.
///
/// Today only the PuppeteerSharp browser-cache path lives here, but the
/// section is a natural home for future report-stack toggles (e.g. PDF
/// margin defaults, Excel banding colours, SP timeout overrides).
/// </summary>
public class ReportOptions
{
    public const string SectionName = "Reports";

    /// <summary>
    /// Where PuppeteerSharp downloads + caches Chromium. Defaults to a
    /// per-user local-app-data folder so the ~500 MB browser survives
    /// <c>dotnet clean</c> and isn't bundled into deploy artefacts.
    ///
    /// Override when the default disk is tight (the original report
    /// from FSI hit "not enough space" on D:\ — the build output
    /// inherited the cache because BrowserFetcher's default is the
    /// current working directory). Point this at a roomy drive in
    /// appsettings.Development.json:
    ///
    ///   "Reports": { "ChromiumCachePath": "E:\\PuppeteerCache" }
    ///
    /// Empty string means "use the resolved default" (see
    /// PuppeteerReportPdfGenerator.ResolveCachePath).
    /// </summary>
    public string ChromiumCachePath { get; set; } = "";

    /// <summary>
    /// Absolute path to an already-installed Chromium-based browser
    /// (Chrome or Edge). When set AND the file exists, the PDF generator
    /// launches THAT browser and SKIPS PuppeteerSharp's ~150 MB first-run
    /// download — required for the OFFLINE deployment server, which has no
    /// internet to fetch Chromium. Microsoft Edge ships with Windows, so the
    /// usual value is:
    ///
    ///   "Reports": {
    ///     "ChromiumExecutablePath": "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"
    ///   }
    ///
    /// Empty = fall back to download/cache behaviour (fine for dev/online).
    /// </summary>
    public string ChromiumExecutablePath { get; set; } = "";
}
