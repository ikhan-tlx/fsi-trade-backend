using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.Get;

public class GetCustomerQueryHandler : IRequestHandler<GetCustomerQuery, CustomerMasterDto>
{
    private readonly ICustomerMasterService _customers;
    public GetCustomerQueryHandler(ICustomerMasterService customers) => _customers = customers;

    public async Task<CustomerMasterDto> Handle(GetCustomerQuery req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerId))
            throw new NotFoundException("customer_not_found", "Customer ID is required.");

        var c = await _customers.GetByCustomerIdAsync(req.CustomerId.Trim(), ct);
        if (c is null)
            throw new NotFoundException("customer_not_found", $"Customer '{req.CustomerId}' not found.");

        return new CustomerMasterDto
        {
            customerCode            = c.CustomerCode,
            customerName            = c.CustomerName,
            customerType            = c.CustomerType,
            nationalIdentifierType  = c.NationalIdentifierType,
            nationalIdentifierValue = c.NationalIdentifierValue,
            emailAddress            = c.EmailAddress,
            phoneNumber             = c.PhoneNumber,
            addressLine1            = c.AddressLine1,
            addressLine2            = c.AddressLine2,
            city                    = c.City,
            country                 = c.Country,
            locationId              = c.LocationId,
            branchCode              = c.BranchCode,
            branchName              = c.BranchName,
            status                  = c.Status,
            registrationDate        = c.RegistrationDate,
            additional              = c.Additional
        };
    }
}
