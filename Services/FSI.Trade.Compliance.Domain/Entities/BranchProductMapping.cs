namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// READ-ONLY projection over <c>TmX_Branch_Products_Mapping</c> — the branch↔product
/// entitlement link (which products a company branch may transact). Used to scope the
/// product-selection LOV to the products the caller's branches are mapped to, mirroring
/// legacy <c>LoanProductService.GetAvailableProductsByUserBranch /
/// GetActiveLoanProductLovByUserBranch</c> (new-transaction product step).
/// </summary>
public class BranchProductMapping
{
    public int   Id              { get; set; }   // Branch_Product_Map_ID — surrogate key
    public int   CompanyBranchId { get; set; }   // Company_Branch_ID
    public int   ProductId       { get; set; }   // Loan_Product_ID (FK → TmX_Product.Product_ID)
    public int   TenantId        { get; set; }   // Tenant_ID
    public bool? ActiveFlag      { get; set; }   // Active_Flag (nullable in legacy schema)
}
