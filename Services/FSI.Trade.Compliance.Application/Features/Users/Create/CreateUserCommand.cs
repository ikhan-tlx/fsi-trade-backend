using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.Create;

public class CreateUserCommand : IRequest<string>
{
    public string  userName     { get; set; } = "";
    public string  password     { get; set; } = "";
    public string? emailAddress { get; set; }
    public string? firstName    { get; set; }
    public string? middleName   { get; set; }
    public string? lastName     { get; set; }
    public string? phoneNumber  { get; set; }
    public int?    locationId   { get; set; }

    public List<int> roleIds    { get; set; } = new();
}
