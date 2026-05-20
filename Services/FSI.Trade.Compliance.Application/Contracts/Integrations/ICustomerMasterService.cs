namespace FSI.Trade.Compliance.Application.Contracts.Integrations;

/// <summary>
/// Reads a customer's full master record. Used by the FE Transaction add
/// screen for TBAML products that need enhanced due-diligence data
/// pre-populated.
/// </summary>
public interface ICustomerMasterService
{
    /// <summary>Returns the customer master record, or null if the customer doesn't exist.</summary>
    Task<CustomerMaster?> GetByCustomerIdAsync(string customerId, CancellationToken ct = default);
}

/// <summary>
/// Full customer master record. Field set is illustrative — the live upstream
/// schema will likely have more fields; refine in Slice 4 build phase once
/// real responses are available. Unknown / unused fields go into
/// <see cref="Additional"/> verbatim.
/// </summary>
public class CustomerMaster
{
    public string  CustomerCode             { get; set; } = "";
    public string? CustomerName             { get; set; }
    public string? CustomerType             { get; set; }
    public string? NationalIdentifierType   { get; set; }
    public string? NationalIdentifierValue  { get; set; }
    public string? EmailAddress             { get; set; }
    public string? PhoneNumber              { get; set; }
    public string? AddressLine1             { get; set; }
    public string? AddressLine2             { get; set; }
    public string? City                     { get; set; }
    public string? Country                  { get; set; }
    public int?    LocationId               { get; set; }
    public string? BranchCode               { get; set; }
    public string? BranchName               { get; set; }
    public string? Status                   { get; set; }
    public DateTime? RegistrationDate       { get; set; }

    /// <summary>Free-form pass-through for upstream fields not yet modelled.</summary>
    public Dictionary<string, string?> Additional { get; set; } = new();
}
