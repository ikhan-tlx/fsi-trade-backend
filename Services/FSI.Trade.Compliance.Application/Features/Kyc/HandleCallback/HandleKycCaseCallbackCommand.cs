using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.HandleCallback;

/// <summary>
/// Inbound FCCM callback. Wire shape matches the legacy
/// <c>updateTrnRequest</c> verbatim — see
/// <c>Integration KYC/Controllers/updateTransactionStatusAPIController.cs</c>:
/// <c>caseID</c> + <c>actionCode</c> as strings. JSON deserialization is
/// case-insensitive so <c>caseID</c>, <c>caseId</c>, and <c>CASEID</c> all
/// bind.
///
/// Legacy + current action codes:
/// <list type="bullet">
///   <item><c>"30004"</c> / <c>"983"</c> — Approve.</item>
///   <item><c>"30003"</c> / <c>"984"</c> — Reject.</item>
///   <item>Any other value — legacy's "send back to analyst" branch (no-op
///         in the new BE until a corresponding workflow command exists).</item>
/// </list>
/// </summary>
public class HandleKycCaseCallbackCommand : IRequest<Unit>
{
    public string?  caseID       { get; set; }
    public string?  actionCode   { get; set; }       // string per legacy contract; matches FCCM wire payload
    public string?  actionReason { get; set; }
}
