using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Detail;

/// <summary>
/// Loads one transaction and everything the FE's edit page needs to render
/// the dynamic form. Fans out across eight child tables in independent
/// queries (EF Core can run them sequentially on the same DbContext), parses
/// each UDF JSON payload server-side (decision A), and folds in the
/// workflow snapshot (current state + commands available to the caller)
/// per decision C.
///
/// The legacy <c>TransactionService.GetById</c> used multi-Include EF6
/// joins; here we issue separate queries because:
///
/// <list type="bullet">
///   <item>Each child fans out to a different number of rows; a single
///         multi-Include in EF Core 8 creates a Cartesian explosion that's
///         worse on the wire than N small queries.</item>
///   <item>Child tables don't share FK paths (customer banking is under
///         TransactionCustomerSnapshot, checklist is direct on Transaction, etc.) so the
///         single-SELECT model doesn't simplify code anyway.</item>
/// </list>
///
/// Module_Code constant: legacy uses <c>"5"</c> for Transaction in
/// <c>TmX_Application_Checklist</c> / <c>TmX_Application_Remark</c> /
/// <c>TmX_Application_Deviation</c>. Kept identical for back-compat.
/// </summary>
public class GetTransactionByIdQueryHandler
    : IRequestHandler<GetTransactionByIdQuery, TransactionDetailDto>
{
    /// <summary>Module_Code value identifying "Transaction" rows in shared application tables.</summary>
    public const string TransactionModuleCode = "5";

    private readonly IApplicationDbContext _db;
    private readonly IWorkflowEngine       _engine;
    private readonly ICurrentUserService   _current;

    public GetTransactionByIdQueryHandler(
        IApplicationDbContext db,
        IWorkflowEngine       engine,
        ICurrentUserService   current)
    {
        _db      = db;
        _engine  = engine;
        _current = current;
    }

    public async Task<TransactionDetailDto> Handle(GetTransactionByIdQuery req, CancellationToken ct)
    {
        var id = req.TransactionId;

        // ---------- 1. Transaction header ----------
        var tx = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == id, ct)
            ?? throw new NotFoundException("transaction_not_found", $"Transaction {id} does not exist.");

        // ---------- 2. UDF + child collections (parallel-safe per scoped DbContext: run sequentially) ----------
        var detail        = await _db.TransactionDetails.AsNoTracking()
                                .Where(d => d.TransactionId == id).FirstOrDefaultAsync(ct);
        var beneficiaries = await _db.BeneficiaryDetails.AsNoTracking()
                                .Where(b => b.TransactionId == id).ToListAsync(ct);
        var stakeholders  = await _db.TransactionStakeholders.AsNoTracking()
                                .Where(s => s.TransactionId == id).ToListAsync(ct);

        // ---------- 3. Customer master + banking ----------
        var customers     = await _db.TransactionCustomerSnapshots.AsNoTracking()
                                .Where(c => c.TransactionId == id).ToListAsync(ct);
        var customerIds   = customers.Select(c => c.CustomerMasterId).ToList();
        var bankingRows   = customerIds.Count == 0
                                ? new List<Domain.Entities.Customer.CustomerBankingDetail>()
                                : await _db.CustomerBankingDetails.AsNoTracking()
                                    .Where(b => b.CustomerMasterId.HasValue && customerIds.Contains(b.CustomerMasterId.Value))
                                    .ToListAsync(ct);
        var bankingByCust = bankingRows
            .GroupBy(b => b.CustomerMasterId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ---------- 4. Checklist, remarks, deviations (all filtered by Module_Code = "5") ----------
        var checklist  = await _db.ApplicationChecklists.AsNoTracking()
                            .Where(c => c.TransactionId == id && c.ModuleCode == TransactionModuleCode)
                            .ToListAsync(ct);
        var remarks    = await _db.ApplicationRemarks.AsNoTracking()
                            .Where(r => r.TransactionId == id && r.ModuleCode == TransactionModuleCode)
                            .OrderByDescending(r => r.CreatedDate)
                            .ToListAsync(ct);
        var deviations = await _db.ApplicationDeviations.AsNoTracking()
                            .Where(d => d.TransactionId == id && d.ModuleCode == TransactionModuleCode)
                            .ToListAsync(ct);

        // ---------- 4.5 Flags (Slice 8) ----------
        // Project every active scope row for this product LEFT-JOINed to
        // the transaction's existing flag rows. Flags without a
        // TmX_Transaction_Flag row come back as isFlagged=false so the FE
        // panel always shows the full applicable set.
        var flags = await TransactionFlagProjection.LoadAsync(_db, id, tx.ProductId, ct);

        // ---------- 5. Workflow snapshot ----------
        var workflow = new WorkflowSnapshotDto { processInstanceId = tx.ProcessInstanceId };

        if (tx.ProcessInstanceId.HasValue)
        {
            // Current state via DB (cheap, doesn't load the engine instance).
            workflow.currentState = await _db.WorkflowProcessInstances.AsNoTracking()
                .Where(p => p.Id == tx.ProcessInstanceId.Value)
                .Select(p => p.StateName)
                .FirstOrDefaultAsync(ct);

            // Activity + commands via the engine (vendor-bound).
            try
            {
                workflow.activityName = await _engine.GetCurrentActivityNameAsync(tx.ProcessInstanceId.Value, ct);
            }
            catch { /* engine may not know the process — leave activityName null */ }

            var userId = _current.UserId;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                try
                {
                    var cmds = await _engine.GetAvailableCommandsAsync(tx.ProcessInstanceId.Value, userId, ct);
                    workflow.commands = cmds.ToList();
                }
                catch { /* engine miss — empty commands list is the safe default */ }
            }
        }

        // ---------- 6. Compose response ----------
        return new TransactionDetailDto
        {
            transactionId          = tx.TransactionId,
            tenantId               = tx.TenantId,
            companyBranchId        = tx.CompanyBranchId,
            productId              = tx.ProductId,
            transactionNumber      = tx.TransactionNumber,
            clientReferenceNumber  = tx.ClientReferenceNumber,
            userId                 = tx.UserId,
            currencyId             = tx.CurrencyId,
            transactionStatusLkp   = tx.TransactionStatusLkp,
            transactionTypeLkp     = tx.TransactionTypeLkp,
            transactionDate        = tx.TransactionDate,
            processInstanceId      = tx.ProcessInstanceId,
            isWorkflowAttached     = tx.IsWorkflowAttached,
            createdDate            = tx.CreatedDate,
            createdBy              = tx.CreatedBy,
            lastUpdatedDate        = tx.LastUpdatedDate,
            lastUpdatedBy          = tx.LastUpdatedBy,

            udfData                = ParseJson(detail?.UdfData),

            customers              = customers.Select(c => new CustomerDto
            {
                customerMasterId        = c.CustomerMasterId,
                customerCode            = c.CustomerCode,
                customerName            = c.CustomerName,
                customerTitle           = c.CustomerTitle,
                nationalIdentifierValue = c.NationalIdentifierValue,
                customerTypeLkp         = c.CustomerTypeLkp,
                customerStatusLkp       = c.CustomerStatusLkp,
                customerSegmentLkp      = c.CustomerSegmentLkp,
                customerSubSegmentLkp   = c.CustomerSubSegmentLkp,
                locationId              = c.LocationId,
                udfData                 = ParseJson(c.UdfData),
                bankingDetails          = bankingByCust.TryGetValue(c.CustomerMasterId, out var rows)
                    ? rows.Select(b => new BankingAccountDto
                        {
                            customerBankingDetailId          = b.CustomerBankingDetailId,
                            bankAccountNumber                = b.BankAccountNumber,
                            branchCode                       = b.BranchCode,
                            bankCardNumber                   = b.BankCardNumber,
                            cardMemberName                   = b.CardMemberName,
                            addressTypeLkpId                 = b.AddressTypeLkpId,
                            internetBanking                  = b.InternetBanking,
                            internetBankingTransactionAmount = b.InternetBankingTransactionAmount,
                            internetAtmTransactionAmount     = b.InternetAtmTransactionAmount,
                            mailingCommunication             = b.MailingCommunication,
                            chequeBookNumber                 = b.ChequeBookNumber,
                            udfData                          = ParseJson(b.UdfData)
                        }).ToList()
                    : new List<BankingAccountDto>()
            }).ToList(),

            beneficiaries = beneficiaries.Select(b => new UdfChildDto
            {
                id      = b.BeneficiaryDetailId,
                udfData = ParseJson(b.UdfData)
            }).ToList(),

            stakeholders = stakeholders.Select(s => new UdfChildDto
            {
                id      = s.TransactionStakeholderId,
                udfData = ParseJson(s.UdfData)
            }).ToList(),

            checklist = checklist.Select(c => new ChecklistDto
            {
                applicationChecklistId = c.ApplicationChecklistId,
                tabId                  = c.TabId,
                checklistTypeLkp       = c.ChecklistTypeLkp,
                attachmentUrl          = c.AttachmentUrl,
                verificationRequired   = c.VerificationRequired,
                verificationOutcomeLkp = c.VerificationOutcomeLkp,
                userId                 = c.UserId
            }).ToList(),

            remarks = remarks.Select(r => new RemarkDto
            {
                applicationRemarkId = r.ApplicationRemarkId,
                actionType          = r.ActionType,
                remarksLkp          = r.RemarksLkp,
                comments            = r.Comments,
                userId              = r.UserId,
                createdDate         = r.CreatedDate,
                createdBy           = r.CreatedBy
            }).ToList(),

            deviations = deviations.Select(d => new DeviationDto
            {
                deviationId     = d.DeviationId,
                approvalId      = d.ApprovalId,
                creator         = d.Creator,
                ruleName        = d.RuleName,
                ruleMessage     = d.RuleMessage,
                deviationAction = d.DeviationAction,
                userId          = d.UserId
            }).ToList(),

            flags = flags,

            workflow = workflow
        };
    }

    /// <summary>
    /// Parses the UDF JSON column. Returns null for null/whitespace input.
    /// On parse error (legacy garbage in the DB), returns the raw string
    /// wrapped so the FE can detect + display "corrupt UDF" rather than the
    /// whole endpoint failing.
    /// </summary>
    private static object? ParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            // Clone the root so it stays valid after `doc` is disposed; serializing
            // a JsonElement preserves the structured shape for the response envelope.
            return JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch (JsonException)
        {
            return new { _udfParseError = true, raw };
        }
    }
}
