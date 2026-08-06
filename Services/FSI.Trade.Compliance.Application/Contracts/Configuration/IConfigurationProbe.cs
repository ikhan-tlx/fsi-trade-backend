namespace FSI.Trade.Compliance.Application.Contracts.Configuration;

// Verification probes for the Configuration screen. Implemented in Infrastructure
// (Oracle connect, HTTP reachability, onboarding dry-run, file parse). Results are
// SANITISED — never a connection string, secret, auth header, or unredacted PII.

public record ProbeResult(bool Ok, long LatencyMs, string? Message, string? Code);
// StatusOk = the response is a 2xx; Reachable = the host answered at all (even 4xx/5xx).
// These are DISTINCT — a 404 is Reachable but not StatusOk.
public record UrlProbeResult(bool Reachable, long LatencyMs, int? HttpStatus, string? StatusText, bool StatusOk, string? Message);
public record OnboardingCallResult(object? Request, object? RawResponse, object? Mapped, string? UnmappedValue, string? Error);
public record FileValidationError(string Path, string Message);
public record FileValidationResult(bool Valid, IReadOnlyList<FileValidationError> Errors);

public interface IConfigurationProbe
{
    Task<ProbeResult>          TestFccmConnectionAsync(CancellationToken ct);
    Task<UrlProbeResult>       TestUrlAsync(string url, CancellationToken ct);
    Task<OnboardingCallResult> TestOnboardingCallAsync(string customerNo, CancellationToken ct);
    Task<FileValidationResult> ValidateFileAsync(string schemaKey, string content, CancellationToken ct);

    /// <summary>Execute the configured provider profile with a sample context and
    /// return the full request/response/mapping/timing envelope (test harness).</summary>
    Task<ProviderExecResult>   TestProviderAsync(IDictionary<string, string?> context, CancellationToken ct);
}
