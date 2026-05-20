using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.SubmitCase;

public class SubmitKycCaseCommand : IRequest<KycCaseSubmissionResult>
{
    public string customerId    { get; set; } = "";
    public long?  transactionId { get; set; }
}
