using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.HandleCallback;

public class HandleKycCaseCallbackCommandHandler : IRequestHandler<HandleKycCaseCallbackCommand, Unit>
{
    private readonly IKycCaseService _cases;
    public HandleKycCaseCallbackCommandHandler(IKycCaseService cases) => _cases = cases;

    public async Task<Unit> Handle(HandleKycCaseCallbackCommand req, CancellationToken ct)
    {
        await _cases.HandleCallbackAsync(new KycCaseCallback
        {
            FccmCaseId   = req.fccmCaseId,
            ActionCode   = req.actionCode,
            ActionReason = req.actionReason
        }, ct);
        return Unit.Value;
    }
}
