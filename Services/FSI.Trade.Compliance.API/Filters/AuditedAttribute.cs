namespace FSI.Trade.Compliance.API.Filters;

/// <summary>
/// Marks a controller action for explicit audit logging by <see cref="AuditActionFilter"/>.
/// Use for events the EF interceptor cannot capture: sensitive READs, LOGIN, EXPORT.
/// The action verb + entity type are recorded; the entity key is pulled from the
/// named route value (default "id" / "code") or, failing that, a username-like
/// action argument.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class AuditedAttribute : Attribute
{
    public string  Action     { get; }
    public string  EntityType { get; }
    public string? RouteKey   { get; }

    public AuditedAttribute(string action, string entityType, string? routeKey = null)
    {
        Action     = action;
        EntityType = entityType;
        RouteKey   = routeKey;
    }
}
