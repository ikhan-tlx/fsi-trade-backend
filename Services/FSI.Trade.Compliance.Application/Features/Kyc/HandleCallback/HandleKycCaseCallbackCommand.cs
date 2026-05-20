using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.HandleCallback;

public class HandleKycCaseCallbackCommand : IRequest<Unit>
{
    public string?  fccmCaseId   { get; set; }
    public int      actionCode   { get; set; }       // 30004 = approve, 30003 = reject
    public string?  actionReason { get; set; }
}
