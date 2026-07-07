using System.Text.Json;
using FluentValidation.Results;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using FSI.Trade.Compliance.Domain.Entities.Customer;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Create;

/// <summary>
/// Mirrors the legacy <c>POST /Transaction/Create</c> orchestration:
/// generate IDs, INSERT transaction + customer snapshot, kick off workflow
/// synchronously, return the full detail DTO.
///
/// Sync workflow init by design — see Slice 6 Step 3 decision 4 in
/// FE_CHANGES_REQUIRED.md. The FE waits for the response anyway (it
/// navigates to the edit page using the returned transactionId on success).
///
/// Error handling: if the workflow engine throws during
/// <c>CreateInstanceAsync</c>, the transaction row is already committed.
/// We log the failure, leave <c>IsWorkflowAttached = false</c>, and rethrow
/// so the caller gets a clean 500. The orphan row can be cleaned up
/// manually (or re-attached by a future "initiate workflow" endpoint).
/// </summary>
public class CreateTransactionCommandHandler
    : IRequestHandler<CreateTransactionCommand, TransactionDetailDto>
{
    private readonly IApplicationDbContext           _db;
    private readonly ICurrentUserService             _current;
    private readonly IWorkflowEngine                 _engine;
    private readonly ITransactionNumberGenerator    _txnNumberGen;
    private readonly IMediator                       _mediator;
    private readonly ILogger<CreateTransactionCommandHandler> _log;

    // KYC product is identified by name (env/id-agnostic) — same signal used by
    // ExecuteWorkflowCommandHandler for the case-insertion branch.
    private const string KycProductName = "KYC";

    // Terminal APPLICATION_STATUS labels that let a customer's KYC be re-created:
    // a "success" bucket (Approved/Completed/…) and a "closed" bucket
    // (Rejected/Cancelled/…). Mirrors the Completed/Cancelled roster in
    // ExecuteWorkflowCommandHandler. Workflow-side fallback for the create gate.
    private static readonly string[] KycTerminalStatusNames =
    {
        "Completed", "Approved", "Authorized", "Active",
        "Application Cancelled", "Cancelled", "Discarded", "Rejected",
    };

    public CreateTransactionCommandHandler(
        IApplicationDbContext         db,
        ICurrentUserService           current,
        IWorkflowEngine               engine,
        ITransactionNumberGenerator   txnNumberGen,
        IMediator                     mediator,
        ILogger<CreateTransactionCommandHandler> log)
    {
        _db           = db;
        _current      = current;
        _engine       = engine;
        _txnNumberGen = txnNumberGen;
        _mediator     = mediator;
        _log          = log;
    }

    public async Task<TransactionDetailDto> Handle(CreateTransactionCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated",
                            "Transaction create requires an authenticated caller.");

        // 1. Resolve branch (validate caller is mapped to it OR the branch exists at all).
        //    Per Slice 6 Step 3 decision 2, the FE always passes companyBranchId — we
        //    just validate. Effective-date check on the user-branch mapping mirrors
        //    legacy intent (only count an active assignment).
        var now = DateTime.UtcNow;
        var branch = await _db.CompanyBranchUserMappings.AsNoTracking()
            .Where(m => m.UserId             == userId
                     && m.CompanyBranchId    == req.companyBranchId
                     && m.ActiveFlag
                     && m.EffectiveStartDate <= now
                     && m.EffectiveEndDate   >= now)
            .Select(m => new { m.CompanyBranchId })
            .FirstOrDefaultAsync(ct);

        if (branch is null)
            throw new ValidationException(new[]
            {
                new ValidationFailure("companyBranchId",
                    $"User {userId} is not actively mapped to companyBranchId {req.companyBranchId}.")
            });

        // Branch code drives the transaction-number prefix (legacy format
        // "{BranchCode}{8-digit-seq}" e.g. "TLX00000123"). Pulled from the
        // CompanyBranch entity we mapped specifically for this use case.
        var branchCode = await _db.CompanyBranches.AsNoTracking()
            .Where(c => c.CompanyBranchId == req.companyBranchId)
            .Select(c => c.BranchCode)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ValidationException(new[]
            {
                new ValidationFailure("companyBranchId",
                    $"Branch {req.companyBranchId} has no Branch_Code configured.")
            });

        // 2. Resolve product → workflow scheme code (if any).
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.ProductId == req.productId)
            .Select(p => new { p.ProductId, p.ProductName, p.WorkflowSchemeCode, p.ActiveFlag })
            .FirstOrDefaultAsync(ct);
        if (product is null)
            throw new NotFoundException("product_not_found",
                $"Product {req.productId} does not exist.");

        // 2b. Sub-product is mandatory at create for products that have a
        // ProductTab selector (Trade). KYC / single-tab products have no
        // selector, so subProductTabId stays optional for them.
        var hasSubProducts = await _db.TenantFieldSetups.AsNoTracking()
            .AnyAsync(f => f.ProductId == req.productId
                        && f.FieldLookupType == "ProductTab"
                        && f.IsActive, ct);
        if (hasSubProducts && req.subProductTabId is null)
            throw new ValidationException(new[]
            {
                new ValidationFailure("subProductTabId",
                    "A sub-product (subProductTabId) must be supplied when creating a transaction for this product.")
            });

        // 2c. One active KYC per customer (legacy-style gate, create-time).
        //     A customer may only have ONE in-progress KYC at a time. A *new*
        //     KYC (e.g. a re-assessment) is allowed only once the prior KYC has
        //     been FINALISED.
        //
        //     Finalisation is read straight off Transaction_Status_Lkp: the FCCM
        //     callback (KycCaseController.Callback → FccmKycCaseService
        //     .HandleCallbackAsync) stamps the KYC transaction's status on
        //     Approve/Reject, so the transaction status IS the callback verdict —
        //     no separate KycCaseRequest read needed. A prior KYC in a terminal
        //     status (Approved/Completed or Rejected/Cancelled) frees a new one;
        //     anything else (Draft, Sent To Authorizer, in-flight FCCM, …) is
        //     still "active" and blocks a second create.
        //
        //     The product was already resolved above; the transaction being
        //     created is itself the KYC product, so we match existing KYC
        //     transactions by that same req.productId (no extra product query).
        var isKycProduct = string.Equals(product.ProductName?.Trim(), KycProductName,
                                          StringComparison.OrdinalIgnoreCase);
        var customerCode = req.customer.customerCode?.Trim();
        if (isKycProduct && !string.IsNullOrWhiteSpace(customerCode))
        {
            // Terminal APPLICATION_STATUS ids (id-agnostic across envs; match by
            // Visible_Value across tenants).
            var terminalNamesLower = KycTerminalStatusNames
                .Select(n => n.ToLowerInvariant()).ToArray();
            var terminalStatusIds = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "APPLICATION_STATUS"
                         && l.IsActive
                         && l.VisibleValue != null
                         && terminalNamesLower.Contains(l.VisibleValue.ToLower()))
                .Select(l => l.Id)
                .ToListAsync(ct);

            // Same KYC product + same customer, still in a non-terminal status.
            var activeKyc = await (
                    from t in _db.Transactions.AsNoTracking()
                    join s in _db.TransactionCustomerSnapshots.AsNoTracking()
                        on (int?)t.TransactionId equals s.TransactionId
                    where t.ProductId == req.productId
                       && s.CustomerCode == customerCode
                       && !terminalStatusIds.Contains(t.TransactionStatusLkp)
                    orderby t.TransactionId descending
                    select new { t.TransactionId, t.TransactionNumber })
                .FirstOrDefaultAsync(ct);

            if (activeKyc is not null)
            {
                _log.LogInformation(
                    "KYC active-gate: blocked new KYC create for customer {CustomerCode} — " +
                    "existing not-yet-finalised KYC TxId={TxId} ({TxNo}).",
                    customerCode, activeKyc.TransactionId, activeKyc.TransactionNumber);
                throw new ConflictException("kyc_active_exists",
                    $"An active KYC already exists for this customer " +
                    $"(transaction {activeKyc.TransactionNumber}). It must be finalised " +
                    $"(Approved or Rejected) before a new KYC can be created.");
            }
        }

        // 3. Generate server-side IDs.
        var processInstanceId = Guid.NewGuid();
        var transactionNumber = await _txnNumberGen.NextAsync(branchCode, ct);

        // 4. Build customer snapshot — riskScore is folded into udf_data as JSON
        //    so we don't need to add a new column. Legacy did the same thing.
        // The wizard sends the BRAINS v2.0 band LABEL ("PEP"/"High"/"Medium"/
        // "Low"). Resolve it to the numeric Hidden_Value via the RISK_BAND
        // lookup and persist the NUMBER on udf_data — storage stays decimal
        // (legacy domain) and the dashboard RiskBandClassifier numeric branch
        // reads it. The RISK_BAND lookup is the authoritative band->number map.
        string? customerUdf = null;
        var bandLabel = req.customer.riskScore?.Trim();
        if (!string.IsNullOrWhiteSpace(bandLabel))
        {
            var hidden = await _db.Lookups
                .AsNoTracking()
                .Where(l => l.TenantId == 1
                         && l.LookupType == "RISK_BAND"
                         && l.VisibleValue != null
                         && l.VisibleValue.ToLower() == bandLabel.ToLower())
                .Select(l => l.HiddenValue)
                .FirstOrDefaultAsync(ct);

            if (decimal.TryParse(hidden, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var riskNum))
            {
                customerUdf = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["RiskScore"] = riskNum
                });
            }
            else
            {
                _log.LogWarning(
                    "RISK_BAND lookup has no numeric Hidden_Value for band '{Band}'; " +
                    "risk score not persisted on the new transaction.", bandLabel);
            }
        }

        // 5. INSERT transaction + customer snapshot in a single SaveChanges.
        var tx = new Transaction
        {
            TenantId              = 1,                                       // Tenant_ID — single-tenant deployment
            CompanyBranchId       = req.companyBranchId,
            ProductId             = req.productId,
            UserId                = userId,
            ClientReferenceNumber = req.clientReferenceNumber,
            CurrencyId            = req.currencyId,
            TransactionStatusLkp  = req.transactionStatusLkp ?? 555,         // legacy "Draft" default
            TransactionTypeLkp    = req.transactionTypeLkp,
            TransactionDate       = req.transactionDate ?? DateTime.UtcNow,
            TransactionNumber     = transactionNumber,
            ProcessInstanceId     = processInstanceId,
            IsWorkflowAttached    = false,                                   // flips to true after engine init
            SubProductTabId       = req.subProductTabId,                     // set when the FE knows the sub-type at create
            CreatedBy             = userId,
            CreatedDate           = DateTime.UtcNow
        };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);                                      // generates Transaction_Id

        var snapshot = new TransactionCustomerSnapshot
        {
            TenantId                = 1,
            TransactionId           = tx.TransactionId,
            CustomerCode            = req.customer.customerCode,
            CustomerName            = req.customer.customerName,
            NationalIdentifierValue = req.customer.nationalIdentifierValue,
            NationalIdTypeLkp       = req.customer.nationalIdTypeLkp,
            CustomerTypeLkp         = req.customer.customerTypeLkp,
            UdfData                 = customerUdf,
            CreatedBy               = userId,
            CreatedDate             = DateTime.UtcNow
        };
        _db.TransactionCustomerSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        // 6. Kick off workflow if the product has a scheme code. Sync — caller
        //    waits because the FE needs the workflow snapshot in the response.
        var schemeCode = product.WorkflowSchemeCode?.Trim();
        if (!string.IsNullOrWhiteSpace(schemeCode))
        {
            try
            {
                await _engine.CreateInstanceAsync(
                    schemeCode: schemeCode,
                    processId:  processInstanceId,
                    identityId: userId,
                    parameters: new Dictionary<string, object?>
                    {
                        ["TransactionId"] = tx.TransactionId,
                        ["ProductId"]     = req.productId,
                        ["CreatorId"]     = userId
                    },
                    ct: ct);

                tx.IsWorkflowAttached = true;
                tx.LastUpdatedBy      = userId;
                tx.LastUpdatedDate    = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _log.LogInformation(
                    "Transaction {TransactionId} created with workflow {SchemeCode}, processInstanceId={ProcessInstanceId}.",
                    tx.TransactionId, schemeCode, processInstanceId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Workflow init FAILED for transaction {TransactionId} (scheme '{SchemeCode}', processInstanceId {ProcessInstanceId}). " +
                    "The transaction row exists in DB with IsWorkflowAttached=false. Manual cleanup or re-attach required.",
                    tx.TransactionId, schemeCode, processInstanceId);
                throw;
            }
        }
        else
        {
            _log.LogInformation(
                "Transaction {TransactionId} created without workflow — product {ProductId} has no WorkflowSchemeCode.",
                tx.TransactionId, req.productId);
        }

        // 7. Return the canonical detail shape — reuses our existing GET handler
        //    so the create response and the page-open response are byte-identical.
        return await _mediator.Send(new GetTransactionByIdQuery(tx.TransactionId), ct);
    }
}
