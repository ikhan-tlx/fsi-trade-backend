using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.GetKyc;

public class GetCustomerKycQueryHandler : IRequestHandler<GetCustomerKycQuery, CustomerKycDto>
{
    private readonly IKycScreeningService _kyc;
    public GetCustomerKycQueryHandler(IKycScreeningService kyc) => _kyc = kyc;

    public async Task<CustomerKycDto> Handle(GetCustomerKycQuery req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerId))
            throw new NotFoundException("customer_not_found", "Customer ID is required.");

        var result = await _kyc.GetKycForCustomerAsync(req.CustomerId.Trim(), ct);
        if (result is null)
            throw new NotFoundException("kyc_not_found", $"No KYC record on file for customer '{req.CustomerId}'.");

        return new CustomerKycDto
        {
            customerId   = req.CustomerId,
            customerName = result.CustomerName,
            riskScore    = result.RiskScore
        };
    }
}
