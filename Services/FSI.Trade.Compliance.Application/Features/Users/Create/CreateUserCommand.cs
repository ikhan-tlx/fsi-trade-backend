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

    /// <summary>
    /// Company-branch assignments (legacy "Company &amp; Branch Association"). Each
    /// row maps the user to a branch with an optional reporting boss. Persisted
    /// to TmX_Company_Branch_Users_Mapping. Empty = no branch assignment.
    /// </summary>
    public List<UserBranchMappingDto> companyBranchMappings { get; set; } = new();
}

public class UserBranchMappingDto
{
    public int       companyBranchId    { get; set; }
    public string?   reportingBossId    { get; set; }

    /// <summary>
    /// Effective window for THIS assignment (legacy "Company &amp; Branch
    /// Association" now exposes these as controls). Null start = today;
    /// null end = open-ended (9999-12-31). The branch LOV filters on this
    /// window, so a closed/future window hides the branch from the user.
    /// </summary>
    public DateTime? effectiveStartDate { get; set; }
    public DateTime? effectiveEndDate   { get; set; }

    /// <summary>Active/inactive toggle for this branch assignment. Null = active.</summary>
    public bool?     isActive           { get; set; }
}
