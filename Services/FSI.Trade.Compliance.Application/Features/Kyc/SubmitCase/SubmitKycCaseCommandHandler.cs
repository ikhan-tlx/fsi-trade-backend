using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.SubmitCase;

public class SubmitKycCaseCommandHandler : IRequestHandler<SubmitKycCaseCommand, KycCaseSubmissionResult>
{
    private readonly IKycCaseService _cases;
    public SubmitKycCaseCommandHandler(IKycCaseService cases) => _cases = cases;

    public Task<KycCaseSubmissionResult> Handle(SubmitKycCaseCommand req, CancellationToken ct)
        => _cases.SubmitAsync(new KycCaseSubmissionRequest
        {
            CustomerId    = req.customerId.Trim(),
            TransactionId = req.transactionId
        }, ct);
}
