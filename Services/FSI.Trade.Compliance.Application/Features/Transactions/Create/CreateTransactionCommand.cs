using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Create;

/// <summary>
/// Bootstraps a new trade transaction. Mirrors the actual FE create payload
/// (verified against <c>tmx-finance-frontend-revamp/src/features/tradeRepository/add</c>):
/// a minimal "start a transaction for this product + customer" intent.
/// UDF data, beneficiaries, stakeholders, banking details all land later
/// via the edit/update endpoint (Slice 6 Step 4).
///
/// On submit:
/// <list type="number">
///   <item>Server generates <c>processInstanceId = Guid.NewGuid()</c>.</item>
///   <item>Server generates a transaction number via <c>ITransactionNumberGenerator</c>
///         (calls <c>dbo.TmX_Transaction_Sequence</c> directly).</item>
///   <item>INSERTs the <c>TmX_Transaction</c> row + a
///         <c>TmX_Customer_Master</c> snapshot (single customer).</item>
///   <item>Looks up <c>TmX_Product.Workflow_Scheme_Code</c>; if non-null,
///         calls <c>IWorkflowEngine.CreateInstanceAsync</c> SYNCHRONOUSLY
///         and sets <c>IsWorkflowAttached = true</c>. If null, the row is
///         created with <c>IsWorkflowAttached = false</c> (workflow can be
///         attached later via a future endpoint).</item>
///   <item>Returns the full <see cref="TransactionDetailDto"/> (same shape
///         as <c>GET /Transaction/{id}</c>) including the initial workflow
///         snapshot.</item>
/// </list>
/// </summary>
public class CreateTransactionCommand : IRequest<TransactionDetailDto>
{
    public int       productId             { get; set; }
    public int       companyBranchId       { get; set; }
    public DateTime? transactionDate       { get; set; }
    public string?   clientReferenceNumber { get; set; }
    public int?      transactionTypeLkp    { get; set; }
    public int?      transactionStatusLkp  { get; set; }
    public int?      currencyId            { get; set; }

    public CreateCustomerInput customer { get; set; } = new();
}

public class CreateCustomerInput
{
    public string?  customerCode            { get; set; }
    public string?  customerName            { get; set; }
    public string?  nationalIdentifierValue { get; set; }
    public int?     nationalIdTypeLkp       { get; set; }
    public int?     customerTypeLkp         { get; set; }
    public decimal? riskScore               { get; set; }   // optional; stored on udf_data if provided
}
