namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.Common;

/// <summary>
/// One branch→product assignment as posted by the FE branch form
/// (<c>TmxBranchProductsMapping: [{ LoanProductId, ProductId }]</c>). Either key
/// carries the product id; <see cref="Resolve"/> picks whichever is set.
/// </summary>
public class BranchProductItemDto
{
    public int LoanProductId { get; set; }
    public int ProductId     { get; set; }

    public int Resolve() => LoanProductId != 0 ? LoanProductId : ProductId;
}
