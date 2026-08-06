namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Daily password-expiry NOTIFICATION scanner tunables (Phase 3). Bound from the
/// <c>PasswordExpiryScan</c> section in appsettings.json via <c>IOptionsMonitor</c>
/// so frequency/threshold edits hot-reload without a restart. The scan is a
/// best-effort emitter over the existing notification infra (no new table): it
/// raises <c>password.expiring</c> / <c>password.expired</c> notifications and is
/// additionally gated on <see cref="PasswordPolicyOptions.EnforceExpiry"/>.
/// </summary>
public class PasswordExpiryScanOptions
{
    public const string SectionName = "PasswordExpiryScan";

    public bool     Enabled      { get; set; } = true;
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Warning thresholds (in days remaining) at which to notify. The
    /// widest value also bounds the candidate query (users within this window
    /// or already expired).</summary>
    public List<int> WarningDays { get; set; } = new() { 14, 7, 3, 1 };

    /// <summary>Let the app settle before the first scan pass.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(2);
}
