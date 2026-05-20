namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Per-request access to the current device context. Reads <c>X-Device-Id</c>
/// header. The <c>DeviceTrackingMiddleware</c> populates and validates the
/// header before any handler runs.
/// </summary>
public interface ICurrentDeviceService
{
    /// <summary>The X-Device-Id header value, or null if not present.</summary>
    string? DeviceId { get; }

    /// <summary>The User-Agent header value, or null if not present.</summary>
    string? UserAgent { get; }
}
