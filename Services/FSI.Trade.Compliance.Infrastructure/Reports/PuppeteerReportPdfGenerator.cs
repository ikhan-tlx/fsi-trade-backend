using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace FSI.Trade.Compliance.Infrastructure.Reports;

/// <summary>
/// PuppeteerSharp (headless Chromium) implementation of
/// <see cref="IReportPdfGenerator"/>.
///
/// Chromium download path:
///   • Default is %LOCALAPPDATA%\FSI.Trade.Compliance\PuppeteerCache
///     (Windows) or ~/.local/share/FSI.Trade.Compliance/PuppeteerCache
///     (Linux/macOS).
///   • PuppeteerSharp's stock default is the process working directory,
///     which under <c>dotnet run</c> resolves to <c>bin\Debug\net8.0\</c>.
///     That folder gets nuked by <c>dotnet clean</c>, forcing a fresh
///     ~150 MB download every time — and bloats build output. So we
///     relocate it outside <c>bin/</c> by default.
///   • Override via <c>Reports:ChromiumCachePath</c> in appsettings when
///     the resolved default drive is tight on space (the original
///     symptom from FSI was "There is not enough space on the disk"
///     because the cache landed in <c>bin\Debug\net8.0\</c> on D:\).
///
/// Lifecycle:
///   • Registered as a singleton. One Chromium instance launched on
///     first PDF request, reused for every subsequent request, disposed
///     on host shutdown via <see cref="IAsyncDisposable"/>.
///   • Concurrent first-hits are funneled through a <see cref="SemaphoreSlim"/>
///     so only one download/launch runs at a time.
///
/// Orientation parsing mirrors the legacy convention:
///   • "L" or "Landscape" (case-insensitive) → landscape
///   • anything else (including null/empty) → portrait
/// </summary>
internal sealed class PuppeteerReportPdfGenerator : IReportPdfGenerator, IAsyncDisposable
{
    private readonly ILogger<PuppeteerReportPdfGenerator> _logger;
    private readonly ReportOptions _options;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private IBrowser? _browser;

    public PuppeteerReportPdfGenerator(
        ILogger<PuppeteerReportPdfGenerator> logger,
        IOptions<ReportOptions> options)
    {
        _logger  = logger;
        _options = options.Value;
    }

    public async Task<byte[]> GenerateAsync(string html, string? pageOrientation, CancellationToken ct = default)
    {
        var browser = await EnsureBrowserAsync(ct);

        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html ?? string.Empty, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
        });

        var landscape = IsLandscape(pageOrientation);
        return await page.PdfDataAsync(new PdfOptions
        {
            Format          = PaperFormat.A4,
            Landscape       = landscape,
            PrintBackground = true,
            MarginOptions   = new MarginOptions
            {
                Top    = "12mm",
                Bottom = "12mm",
                Left   = "10mm",
                Right  = "10mm",
            },
        });
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsClosed: false }) return _browser;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_browser is { IsClosed: false }) return _browser;

            var cachePath = ResolveCachePath(_options.ChromiumCachePath);
            Directory.CreateDirectory(cachePath);

            var fetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Path = cachePath,
            });

            _logger.LogInformation(
                "PuppeteerReportPdfGenerator: ensuring Chromium revision is present at '{Path}' (first-run downloads ~150 MB)",
                cachePath);

            var installed = await fetcher.DownloadAsync();

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless       = true,
                ExecutablePath = installed.GetExecutablePath(),
                Args           = new[] { "--no-sandbox", "--disable-dev-shm-usage" },
            });
            _logger.LogInformation("PuppeteerReportPdfGenerator: Chromium launched, ready for PDF requests");
            return _browser;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Picks the cache path in priority order:
    ///   1. The configured value (if non-empty) — supports env-var
    ///      expansion (e.g. <c>%TEMP%\…</c>).
    ///   2. <c>%LOCALAPPDATA%\FSI.Trade.Compliance\PuppeteerCache</c> on
    ///      Windows / equivalent SpecialFolder.LocalApplicationData on
    ///      Linux+macOS.
    ///   3. Final fallback — temp dir (only if LocalApplicationData
    ///      isn't resolvable, which is unusual).
    ///
    /// Either way, the result is OUTSIDE the project's <c>bin/</c> so
    /// <c>dotnet clean</c> doesn't wipe the cache.
    /// </summary>
    private static string ResolveCachePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Environment.ExpandEnvironmentVariables(configured.Trim());

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            return Path.Combine(localAppData, "FSI.Trade.Compliance", "PuppeteerCache");

        return Path.Combine(Path.GetTempPath(), "FSI.Trade.Compliance", "PuppeteerCache");
    }

    private static bool IsLandscape(string? orientation)
    {
        if (string.IsNullOrWhiteSpace(orientation)) return false;
        var trimmed = orientation.Trim();
        return string.Equals(trimmed, "L", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Landscape", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            try { await _browser.CloseAsync(); } catch { /* best-effort */ }
            _browser = null;
        }
        _initGate.Dispose();
    }
}
