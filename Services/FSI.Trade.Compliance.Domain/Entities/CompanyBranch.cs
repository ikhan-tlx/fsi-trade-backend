namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// READ-MOSTLY projection over <c>TmX_Company_Branch</c>. Slice 6 Step 3
/// uses it to look up the branch code (for transaction-number prefix) and
/// to validate a caller's claimed branch exists. Future slices may extend
/// to support branch CRUD.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 765.
/// </summary>
public class CompanyBranch
{
    public int       CompanyBranchId    { get; set; }
    public int       TenantId           { get; set; }
    public int       CompanyId          { get; set; }
    public string    BranchCode         { get; set; } = "";
    public string?   BranchName         { get; set; }
    public string?   BranchDescription  { get; set; }
    public int?      LocationId         { get; set; }
    public int?      AddressId          { get; set; }
    public bool      ActiveFlag         { get; set; }
    public DateTime  EffectiveStartDate { get; set; }
    public DateTime  EffectiveEndDate   { get; set; }
    public string?   Status             { get; set; }

    public string    CreatedBy          { get; set; } = "";
    public DateTime  CreatedDate        { get; set; }
    public string?   LastUpdatedBy      { get; set; }
    public DateTime? LastUpdatedDate    { get; set; }
}
