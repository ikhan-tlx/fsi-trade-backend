namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Slice 5 — workflow runtime tuning. Bound from the <c>Workflow</c>
/// section in appsettings.json.
///
/// LICENSE NOTE: <see cref="LicenseKey"/> is required at runtime — without
/// it, OptimaJet's <c>WorkflowRuntime.RegisterLicense</c> rejects start-up.
/// Treat the value as a secret. Match the legacy AppSettings <c>WFKey</c>
/// for direct reuse during dev.
/// </summary>
public class WorkflowOptions
{
    public const string SectionName = "Workflow";

    /// <summary>OptimaJet license key. Required for runtime to start.</summary>
    public string LicenseKey { get; set; } = "";

    /// <summary>
    /// Stable runtime ID. The legacy backend hard-coded
    /// <c>{8D38DB8F-F3D5-4F26-A989-4FDD40F32D9D}</c>; reuse it so existing
    /// WorkflowProcessInstance rows continue to address the same runtime.
    /// </summary>
    public Guid RuntimeId { get; set; } = new("8D38DB8F-F3D5-4F26-A989-4FDD40F32D9D");

    /// <summary>
    /// Inbox-page max size. Hard cap to prevent runaway pagination on the
    /// /Workflow/Inbox endpoint.
    /// </summary>
    public int InboxMaxPageSize { get; set; } = 100;
}
