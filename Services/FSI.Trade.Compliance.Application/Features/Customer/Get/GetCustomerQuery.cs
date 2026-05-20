using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.Get;

public record GetCustomerQuery(string CustomerId) : IRequest<CustomerMasterDto>;

/// <summary>
/// Customer master DTO. Field set is a starting point; refine in Slice 4
/// build phase based on actual upstream responses. <see cref="additional"/>
/// holds anything not yet modelled — passed through to the FE verbatim.
/// </summary>
public class CustomerMasterDto
{
    public string  customerCode             { get; set; } = "";
    public string? customerName             { get; set; }
    public string? customerType             { get; set; }
    public string? nationalIdentifierType   { get; set; }
    public string? nationalIdentifierValue  { get; set; }
    public string? emailAddress             { get; set; }
    public string? phoneNumber              { get; set; }
    public string? addressLine1             { get; set; }
    public string? addressLine2             { get; set; }
    public string? city                     { get; set; }
    public string? country                  { get; set; }
    public int?    locationId               { get; set; }
    public string? branchCode               { get; set; }
    public string? branchName               { get; set; }
    public string? status                   { get; set; }
    public DateTime? registrationDate       { get; set; }

    public Dictionary<string, string?> additional { get; set; } = new();
}
