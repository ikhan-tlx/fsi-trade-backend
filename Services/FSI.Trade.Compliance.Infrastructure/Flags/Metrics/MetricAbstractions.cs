using FSI.Trade.Compliance.Domain.Entities.Transaction;

namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>Aggregate = computed over a collection (grid rows / a customer window);
/// Derived = single computed value from this transaction's fields (future track).</summary>
public enum MetricKind { Aggregate, Derived }

/// <summary>One authoring parameter a metric exposes to the FE builder (drives the param form
/// and server-side validation). <see cref="Type"/>: enum | field | number | list.
/// <see cref="FieldFilter"/>: "numeric" restricts a field param to number/amount fields.</summary>
public sealed record MetricParamDef(
    string Name, string Label, string Type,
    string? FieldFilter = null, bool Required = false, string[]? Options = null);

/// <summary>One group in a grouped-aggregate result (enhancement #2). <see cref="Key"/> is the
/// composite group key (e.g. { "LcNumber": "LC-001" }); <see cref="Value"/> is the group's LHS
/// aggregate; <see cref="Rhs"/> is the optional per-group counterpart aggregate (e.g. that LC's
/// amount) the evaluator compares against — null when the rule compares to a static threshold.</summary>
public sealed record MetricGroup(IReadOnlyDictionary<string, string> Key, string Value, string? Rhs)
{
    // Field-vs-counterpart mode (#3): the group's left/right field value sets, compared as sets by
    // the evaluator (Equals/NotEquals/Contains). Null for the numeric aggregate path.
    public IReadOnlyList<string>? LeftSet  { get; init; }
    public IReadOnlyList<string>? RightSet { get; init; }
}

/// <summary>Outcome of computing a metric's left-operand value. Never throws — a
/// non-computable input (missing field, no customer window, non-numeric) yields
/// <see cref="Computable"/> = false so the condition resolves to not-satisfied.
///
/// A metric is either SCALAR (<see cref="Value"/>, the classic path) or GROUPED
/// (<see cref="Groups"/>, enhancement #2 — one aggregate per group key, evaluated per group).</summary>
public sealed class MetricResult
{
    public bool    Computable { get; init; }
    public string? Value      { get; init; }   // invariant-formatted scalar (or first group's value)
    public object? Inputs     { get; init; }   // for the Match_Proof node
    public IReadOnlyList<MetricGroup>? Groups { get; init; }   // set when the metric is grouped

    public static MetricResult Ok(string value, object? inputs = null)
        => new() { Computable = true, Value = value, Inputs = inputs };
    public static MetricResult NotComputable(object? inputs = null)
        => new() { Computable = false, Value = null, Inputs = inputs };

    /// <summary>A grouped result — one aggregate per group. Value mirrors the first group so any
    /// scalar-only consumer stays null-safe; grouped consumers read <see cref="Groups"/>.</summary>
    public static MetricResult Grouped(IReadOnlyList<MetricGroup> groups, object? inputs = null)
        => new() { Computable = true, Value = groups.Count > 0 ? groups[0].Value : null, Groups = groups, Inputs = inputs };
}

/// <summary>Everything a handler needs to compute a metric for one transaction.</summary>
public sealed class MetricContext
{
    public required Transaction Tx { get; init; }
    public required IReadOnlyDictionary<string, List<string>> Udf { get; init; }
    public string? SpecJson    { get; init; }
    public string? CustomerCode { get; init; }
    public required IRuleDataProvider Data { get; init; }

    /// <summary>The point-in-time anchor for windows: the transaction's value date.</summary>
    public DateTime ValueDate => Tx.TransactionDate ?? Tx.CreatedDate;
}

/// <summary>A named, auditable metric. Adding one = add a handler + register it; no
/// evaluator or FE change. Consulted by the evaluator when Left_Source is Aggregate/Derived.</summary>
public interface IRuleMetricHandler
{
    string   Key      { get; }        // e.g. "AGGREGATE", "DAYS_SINCE_LAST_ACTIVITY"
    MetricKind Kind   { get; }
    string   DataType { get; }        // number | date | boolean | code
    IReadOnlyList<MetricParamDef> Params { get; }
    Task<MetricResult> ComputeAsync(MetricContext ctx, CancellationToken ct);
}

/// <summary>One prior transaction in a customer window (for cross-transaction aggregates).</summary>
public sealed record CustomerTxnRow(int TransactionId, DateTime ValueDate, int ProductId, int? SubProductTabId, string? UdfData);

/// <summary>Scoped historical data access for cross-transaction metrics. Compute-on-read —
/// no fact table. Implemented over IApplicationDbContext.</summary>
public interface IRuleDataProvider
{
    Task<string?> ResolveCustomerCodeAsync(int transactionId, CancellationToken ct);

    /// <summary>Transactions for a customer with value date in [fromInclusive, toInclusive].</summary>
    Task<IReadOnlyList<CustomerTxnRow>> GetCustomerWindowAsync(
        string customerCode, DateTime fromInclusive, DateTime toInclusive, CancellationToken ct);

    /// <summary>Latest prior value date for a customer strictly before <paramref name="beforeExclusive"/>,
    /// excluding <paramref name="excludeTransactionId"/>. Null = no prior activity.</summary>
    Task<DateTime?> GetLastActivityDateBeforeAsync(
        string customerCode, DateTime beforeExclusive, int excludeTransactionId, CancellationToken ct);

    /// <summary>The UDF field code(s) marked as the amount field (Is_Amount_Field) for a
    /// product / sub-product tab, from the field configuration. Usually one per sub-product.</summary>
    Task<IReadOnlyList<string>> GetAmountFieldCodesAsync(int productId, int? tabId, CancellationToken ct);

    /// <summary>The customer's LATEST KYC-product transaction's UDF JSON — used to resolve
    /// cross-source KYC operands (e.g. declared Line of Business) at rule-evaluation time.
    /// "Latest" = most recent by value/created date. Null when the customer has no KYC record.</summary>
    Task<string?> GetLatestKycUdfAsync(string customerCode, CancellationToken ct);
}
