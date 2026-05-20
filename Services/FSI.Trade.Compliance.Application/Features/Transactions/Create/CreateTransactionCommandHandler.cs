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
            .Select(p => new { p.ProductId, p.WorkflowSchemeCode, p.ActiveFlag })
            .FirstOrDefaultAsync(ct);
        if (product is null)
            throw new NotFoundException("product_not_found",
                $"Product {req.productId} does not exist.");

        // 3. Generate server-side IDs.
        var processInstanceId = Guid.NewGuid();
        var transactionNumber = await _txnNumberGen.NextAsync(branchCode, ct);

        // 4. Build customer snapshot — riskScore is folded into udf_data as JSON
        //    so we don't need to add a new column. Legacy did the same thing.
        var customerUdf = req.customer.riskScore.HasValue
            ? JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["RiskScore"] = req.customer.riskScore.Value
                })
            : null;

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
