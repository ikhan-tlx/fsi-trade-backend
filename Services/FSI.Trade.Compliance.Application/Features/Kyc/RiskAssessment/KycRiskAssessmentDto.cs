namespace FSI.Trade.Compliance.Application.Features.Kyc.RiskAssessment;

/// <summary>
/// Payload for the KYC "Risk Assessment" tab (Option A). Consolidates the
/// BRAINS + FCCM assessment for the current execution plus the full per-customer
/// execution history. Read-only; the FE renders the three cards (Current
/// Assessment / Processing Lifecycle / Assessment History) from this shape.
/// </summary>
public class KycRiskAssessmentDto
{
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }

    /// <summary>The execution row matching the opened transaction, else the newest.</summary>
    public long?   CurrentRequestId { get; set; }

    /// <summary>Poller cadence surfaced to the lifecycle card ("Poller runs every Ns · M-minute timeout").</summary>
    public int     PollingIntervalSeconds { get; set; }
    public int     CaseIdTimeoutMinutes   { get; set; }

    /// <summary>Convenience flag; the FE also gates the ⋮ menu on its own privilege check.</summary>
    public bool    CanOperate { get; set; }

    public List<KycRiskExecutionDto> Executions { get; set; } = new();
}

public class KycRiskExecutionDto
{
    public long     RequestId  { get; set; }
    public string   RequestRef { get; set; } = "";      // "KYC-REQ-00001"

    public long?    TransactionId       { get; set; }
    public string?  TransactionNumber   { get; set; }
    public string?  ProductLabel        { get; set; }
    public bool     IsCurrentTransaction { get; set; }

    public DateTime SubmittedAt { get; set; }
    public string?  SubmittedBy { get; set; }

    public KycRiskBandDto        Brains     { get; set; } = new();
    public KycRiskBandDto        Fccm       { get; set; } = new();
    public KycReconciledRiskDto  Reconciled { get; set; } = new();

    public string?  FccmCaseId    { get; set; }
    public string?  FccmRequestId { get; set; }

    public string   Status      { get; set; } = "";
    public string?  ErrorDetail { get; set; }

    public KycLifecycleDto Lifecycle { get; set; } = new();
}

public class KycRiskBandDto
{
    public int?    Score { get; set; }   // system-native numeric (BRAINS 0-3, FCCM 1-3)
    public string? Label { get; set; }   // "PEP"/"High"/"Medium"/"Low"; null => not available (FE shows Pending / —)
}

public class KycReconciledRiskDto
{
    public int?    Score            { get; set; }
    public string? Label            { get; set; }
    public string? Display          { get; set; }   // raw Cstm_Brains.Display e.g. "High - TBML"
    public string? PrevailingSource { get; set; }   // "FCCM" | "BRAINS" | "EQUAL" | null
}

public class KycLifecycleDto
{
    public DateTime  SubmittedAt    { get; set; }
    public DateTime? CaseCreatedAt  { get; set; }
    public DateTime? RiskAssessedAt { get; set; }
    public DateTime? LastPolledAt   { get; set; }
    public DateTime  LastUpdatedAt  { get; set; }
}

/// <summary>
/// Band-scale helpers. The two upstreams use different numeric scales:
/// BRAINS 0 PEP / 1 High / 2 Medium / 3 Low; FCCM (TBML) 1 Low / 2 Medium /
/// 3 High. The reconciled Final numeric follows the poller's rule (1 Low /
/// 2 Medium / 3 or 0 → High). Always surface the LABEL to the UI, never the
/// bare number.
/// </summary>
public static class KycRiskBands
{
    public static string? BrainsLabel(int? n) => n switch
    {
        0 => "PEP", 1 => "High", 2 => "Medium", 3 => "Low", _ => null
    };

    public static string? FccmLabel(int? n) => n switch
    {
        1 => "Low", 2 => "Medium", 3 => "High", _ => null
    };

    public static string? FinalLabel(int? n) => n switch
    {
        1 => "Low", 2 => "Medium", 3 => "High", 0 => "High", _ => null
    };

    /// <summary>Parses a Cstm_Brains varchar score column to an int, tolerating null/blank.</summary>
    public static int? ParseScore(string? raw) =>
        int.TryParse((raw ?? "").Trim(), out var v) ? v : (int?)null;

    /// <summary>
    /// Derives which system's band the reconciliation adopted from the legacy
    /// Display suffix: "- TBML" → FCCM, "- BRAINS & TBML SCORES ARE EQUAL" →
    /// EQUAL, "- BRAINS" → BRAINS. Null when unknown.
    /// </summary>
    public static string? PrevailingSource(string? display)
    {
        var d = (display ?? "").ToUpperInvariant();
        if (d.Length == 0) return null;
        if (d.Contains("EQUAL")) return "EQUAL";
        if (d.Contains("TBML"))  return "FCCM";
        if (d.Contains("BRAINS")) return "BRAINS";
        return null;
    }
}
