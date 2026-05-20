using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.GetCaseStatus;

public class GetKycCaseStatusQueryHandler : IRequestHandler<GetKycCaseStatusQuery, KycCaseStatusDto>
{
    private readonly IKycCaseService _cases;
    public GetKycCaseStatusQueryHandler(IKycCaseService cases) => _cases = cases;

    public async Task<KycCaseStatusDto> Handle(GetKycCaseStatusQuery req, CancellationToken ct)
    {
        var status = await _cases.GetStatusAsync(req.RequestId, ct);
        if (status is null)
            throw new NotFoundException("kyc_case_not_found", $"KYC case {req.RequestId} not found.");
        return status;
    }
}
