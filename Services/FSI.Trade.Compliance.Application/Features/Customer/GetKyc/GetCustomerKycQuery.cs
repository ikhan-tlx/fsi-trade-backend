using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.GetKyc;

public record GetCustomerKycQuery(string CustomerId) : IRequest<CustomerKycDto>;

public class CustomerKycDto
{
    public string  customerId   { get; set; } = "";
    public string? customerName { get; set; }
    public string  riskScore    { get; set; } = "";
}
