namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// One declarative condition row of an auto flag's rule set. Maps to
/// <c>dbo.TmX_Flag_Rule</c> (migration 2026_06_040). A flag's rules are
/// combined by <see cref="RuleGroup"/> (AND within a group) across groups
/// (OR), in <see cref="EvaluationOrder"/>. The custom evaluator reads
/// these rows — no per-rule stored procedure.
/// </summary>
public class FlagRule
{
    public int       RuleId            { get; set; }
    public int       FlagId            { get; set; }
    public int       RuleGroup         { get; set; } = 1;
    public int       EvaluationOrder   { get; set; } = 1;
    public string    LogicalOperator   { get; set; } = "AND";   // AND | OR

    /// <summary>Trade | KYC | Transaction | Reference | Aggregate | Derived.</summary>
    public string    LeftSource        { get; set; } = "Trade";
    public string?   LeftFieldCode     { get; set; }

    /// <summary>Equals | NotEquals | In | NotIn | Contains | GreaterThan | LessThan | …</summary>
    public string    Operator          { get; set; } = "NotIn";

    /// <summary>Field | StaticValue | ReferenceList | Threshold | Aggregate | Derived.</summary>
    public string    RightOperandType  { get; set; } = "Field";
    public string?   RightSource       { get; set; }
    public string?   RightFieldCode    { get; set; }
    public string?   StaticValue       { get; set; }
    public string?   ReferenceListCode { get; set; }

    /// <summary>Optional scale applied to the RIGHT operand value:
    /// rhs = fieldValue * (RightFactorUnit == "Percent" ? RightFactor/100 : RightFactor).
    /// Null => x1 (unchanged). Lets a rule read "&gt; KYC field x 1.5" or "&gt; x 60%".</summary>
    public decimal?  RightFactor       { get; set; }
    /// <summary>"Multiplier" | "Percent". Null treated as Multiplier.</summary>
    public string?   RightFactorUnit   { get; set; }

    /// <summary>JSON spec for an aggregate operand, e.g. {"metric":"TxnVolume","window":90}.</summary>
    public string?   AggregateSpec     { get; set; }

    /// <summary>Named handler key for a derived/external operand provider.</summary>
    public string?   HandlerKey        { get; set; }

    public bool      ActiveFlag        { get; set; } = true;
    public string?   CreatedBy         { get; set; }
    public DateTime  CreatedDate       { get; set; }
    public string?   LastUpdatedBy     { get; set; }
    public DateTime? LastUpdatedDate   { get; set; }
}
