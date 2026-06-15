using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Diagnostics;

/// <summary>
/// Periodically archives rolled diagnostic log files. Daily Serilog files are
/// named <c>&lt;name&gt;-yyyyMMdd.log</c>; once a file's date is older than
/// <c>Integration:Diagnostics:RetentionDays</c> (default 30) it is compressed
/// into a single archive named
/// <c>backlogs_&lt;yyyyMMdd FROM&gt;-&lt;yyyyMMdd TO&gt;.zip</c> in the same
/// folder and the original is deleted. The active (recent) files are left
/// untouched. Runs every <c>ArchiveScanIntervalHours</c> (default 12).
///
/// The Serilog file sinks themselves keep all files (retainedFileCountLimit
/// null) — retention/compaction is owned here so nothing is lost before it is
/// archived.
/// </summary>
public sealed class LogArchiverService : BackgroundService
{
    private readonly IConfiguration              _config;
    private readonly ILogger<LogArchiverService> _log;

    private static readonly Regex DatedLog = new(@"-(\d{8})\.log$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LogArchiverService(IConfiguration config, ILogger<LogArchiverService> log)
    {
        _config = config;
        _log    = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var diag = _config.GetSection("Integration:Diagnostics");
        var dir  = diag["LogDirectory"];
        if (string.IsNullOrWhiteSpace(dir)) return;                      // logging disabled
        if (!Path.IsPathRooted(dir)) dir = Path.Combine(AppContext.BaseDirectory, dir);

        var retentionDays = Math.Max(1, diag.GetValue("RetentionDays", 30));
        var intervalHours = Math.Max(1, diag.GetValue("ArchiveScanIntervalHours", 12));

        // Small startup delay so this never competes with app boot.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stop); } catch { return; }

        _log.LogInformation("LogArchiverService started. Dir={Dir}, RetentionDays={Days}, ScanEvery={Hours}h.",
            dir, retentionDays, intervalHours);

        while (!stop.IsCancellationRequested)
        {
            try { ArchiveOnce(dir, retentionDays); }
            catch (Exception ex) { _log.LogError(ex, "Log archive run failed."); }

            try { await Task.Delay(TimeSpan.FromHours(intervalHours), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Single archive pass — exposed internal for testing/forced runs.</summary>
    internal void ArchiveOnce(string dir, int retentionDays)
    {
        if (!Directory.Exists(dir)) return;
        var cutoff = DateTime.Today.AddDays(-retentionDays);

        var stale = new List<(string Path, DateTime Date)>();
        foreach (var file in Directory.GetFiles(dir, "*.log"))
        {
            var m = DatedLog.Match(Path.GetFileName(file));
            if (!m.Success) continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
            if (d.Date < cutoff) stale.Add((file, d.Date));
        }
        if (stale.Count == 0) return;

        var from    = stale.Min(x => x.Date);
        var to      = stale.Max(x => x.Date);
        var zipName = $"backlogs_{from:yyyyMMdd}-{to:yyyyMMdd}.zip";
        var zipPath = Path.Combine(dir, zipName);

        var isUpdate = File.Exists(zipPath);
        using (var zip = ZipFile.Open(zipPath, isUpdate ? ZipArchiveMode.Update : ZipArchiveMode.Create))
        {
            foreach (var (path, _) in stale)
            {
                var entry = Path.GetFileName(path);
                // GetEntry is only valid in Update/Read mode — skip the dup check
                // when creating a fresh archive.
                if (isUpdate && zip.GetEntry(entry) != null) continue;  // already archived
                zip.CreateEntryFromFile(path, entry, CompressionLevel.Optimal);
            }
        }

        var deleted = 0;
        foreach (var (path, _) in stale)
        {
            try { File.Delete(path); deleted++; }
            catch (Exception ex) { _log.LogWarning("Could not delete archived log {File}: {Err}", path, ex.Message); }
        }

        _log.LogInformation("Archived {Count} rolled log file(s) [{From:yyyyMMdd}–{To:yyyyMMdd}] into {Zip} (deleted {Deleted}).",
            stale.Count, from, to, zipName, deleted);
    }
}
