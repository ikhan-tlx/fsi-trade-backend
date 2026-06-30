namespace FSI.Trade.Compliance.Application.Contracts.Flags;

/// <summary>
/// Configuration-driven auto red-flag engine. Evaluates the active Auto flags
/// (TmX_Flag_Catalogue.Flag_Type=Automated + their TmX_Flag_Rule rows) in scope
/// for a transaction. P1 covers the reference-list / field / static-value
/// archetypes — the generalisation of the legacy country/HS sanction screening
/// (REPO_SANCTIONED_COUNTRIES / REPO_SCREENING_GOODS).
/// </summary>
public interface IAutoFlagEvaluator
{
    /// <summary>
    /// Evaluate every active auto flag in scope for the transaction and STAGE
    /// the resulting TmX_Transaction_Flag (Source=Auto) upserts onto the shared
    /// DbContext — the caller's SaveChanges commits them atomically with the
    /// rest of the transaction save. Returns the hits for logging.
    /// </summary>
    Task<IReadOnlyList<AutoFlagHit>> EvaluateAndStageAsync(
        int transactionId, string userId, DateTime now, CancellationToken ct);

    /// <summary>
    /// Dry-run a single flag's rules against a transaction without writing —
    /// backs the rule-builder "Test" panel.
    /// </summary>
    Task<AutoFlagDryRun> DryRunAsync(int flagId, int transactionId, CancellationToken ct);
}

/// <summary>One raised auto flag. Per-match flags produce several of these for the
/// same FlagId (one per matching field × value); aggregate flags produce one.</summary>
public sealed class AutoFlagHit
{
    public int     FlagId       { get; set; }
    public int?    RuleId       { get; set; }
    public string  MatchedValue { get; set; } = "";
}

/// <summary>Dry-run outcome for the Test panel.</summary>
public sealed class AutoFlagDryRun
{
    public bool       WouldRaise   { get; set; }
    public List<int>  MatchedRules { get; set; } = new();
    public string?    Detail       { get; set; }

    // 2026_06_061 — surfaced in the rule-builder Test panel.
    /// <summary>TRUE when the flag is configured to raise one flag per matching value.</summary>
    public bool         PerMatch      { get; set; }
    /// <summary>The matched (field × value) tokens the flag would raise on.</summary>
    public List<string> MatchedValues { get; set; } = new();
    /// <summary>Developer/QA JSON trace of the full evaluation (the "proof").</summary>
    public string?      Proof         { get; set; }
}
