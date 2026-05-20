using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.Update;

public class UpdateUserCommand : IRequest<Unit>
{
    public string  userId       { get; set; } = "";
    public string? emailAddress { get; set; }
    public string? firstName    { get; set; }
    public string? middleName   { get; set; }
    public string? lastName     { get; set; }
    public string? phoneNumber  { get; set; }
    public int?    locationId   { get; set; }

    /// <summary>Replaces the user's role mapping atomically. Send the FULL desired set.</summary>
    public List<int>? roleIds   { get; set; }
}
