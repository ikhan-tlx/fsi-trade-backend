namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>DI-populated registry of metric handlers, keyed by Handler_Key (case-insensitive).
/// Adding a metric = register a handler; the evaluator resolves by key and the
/// GET /AutoFlag/Metrics endpoint reflects the catalogue. No evaluator/FE change.</summary>
public sealed class MetricHandlerRegistry
{
    private readonly Dictionary<string, IRuleMetricHandler> _byKey;

    public MetricHandlerRegistry(IEnumerable<IRuleMetricHandler> handlers)
        => _byKey = handlers.ToDictionary(h => h.Key, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string? key, out IRuleMetricHandler handler)
    {
        handler = default!;
        return !string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key!, out handler!);
    }

    public IReadOnlyCollection<IRuleMetricHandler> All => _byKey.Values;
}
