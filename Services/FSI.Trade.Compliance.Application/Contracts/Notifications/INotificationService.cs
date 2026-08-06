namespace FSI.Trade.Compliance.Application.Contracts.Notifications;

/// <summary>
/// Fire-and-forget notification emitter. Every call MUST happen AFTER the
/// business write has committed (its own SaveChangesAsync) — a notification
/// insert must never share a transaction with, or be able to roll back, the
/// approval/assignment it describes. The implementation swallows and logs its
/// own exceptions so a notification failure can never surface to the caller.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Raise one notification row per distinct recipient. Recipients equal to
    /// <paramref name="actor"/> (case-insensitive), null or blank are skipped —
    /// never notify the actor about their own action. Never throws.
    /// </summary>
    Task RaiseAsync(IEnumerable<string> recipients, string typeCode, string severity, string title,
                    string? body, string? entityType, string? entityId, string? deepLink,
                    string? actor, string? groupKey, int? tenantId, CancellationToken ct = default);
}
