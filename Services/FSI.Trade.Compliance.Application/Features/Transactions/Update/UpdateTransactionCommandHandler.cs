using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Flags.Write;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using FSI.Trade.Compliance.Domain.Entities.Customer;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Update;

/// <summary>
/// Saves the current state of an existing transaction. Mirrors legacy
/// <c>TransactionService.UpdateTransaction</c> semantics: header update,
/// UDF replacement, customer + banking details, beneficiaries, stakeholders.
///
/// Workflow state is deliberately NOT touched here. Callers that also need
/// to advance the workflow call <c>PUT /Workflow/Process/{id}/Execute</c>
/// after this endpoint returns. That's the FE's save-then-execute pattern
/// in <c>dynamicForm.tsx</c>.
///
/// <para>
/// <b>Diff strategy on child collections</b> (beneficiaries, stakeholders,
/// customer banking details): rows whose id appears in the request are
/// UPDATEd; rows without an id are INSERTed; existing rows whose id is
/// NOT in the request are DELETEd. This matches the legacy "replace whole
/// child collection" semantic without forcing us to delete+re-insert
/// (which would burn IDENTITY values).
/// </para>
/// </summary>
public class UpdateTransactionCommandHandler
    : IRequestHandler<UpdateTransactionCommand, TransactionDetailDto>
{
    private readonly IApplicationDbContext   _db;
    private readonly ICurrentUserService     _current;
    private readonly IMediator               _mediator;
    private readonly ILogger<UpdateTransactionCommandHandler> _log;

    public UpdateTransactionCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current,
        IMediator             mediator,
        ILogger<UpdateTransactionCommandHandler> log)
    {
        _db       = db;
        _current  = current;
        _mediator = mediator;
        _log      = log;
    }

    public async Task<TransactionDetailDto> Handle(UpdateTransactionCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Transaction update requires an authenticated caller.");

        var now = DateTime.UtcNow;

        // ---------- 1. Header ----------
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == req.transactionId, ct)
                 ?? throw new NotFoundException("transaction_not_found",
                        $"Transaction {req.transactionId} does not exist.");

        if (req.transactionDate.HasValue)      tx.TransactionDate     = req.transactionDate.Value;
        if (req.currencyId.HasValue)           tx.CurrencyId          = req.currencyId.Value;
        if (req.transactionTypeLkp.HasValue)   tx.TransactionTypeLkp  = req.transactionTypeLkp.Value;
        if (req.transactionStatusLkp.HasValue) tx.TransactionStatusLkp = req.transactionStatusLkp.Value;
        if (req.clientReferenceNumber is not null) tx.ClientReferenceNumber = req.clientReferenceNumber;
        tx.LastUpdatedBy   = userId;
        tx.LastUpdatedDate = now;

        // ---------- 2. Transaction detail (UDF) ----------
        await UpsertTransactionDetailAsync(req.transactionId, req.udfData, userId, now, ct);

        // ---------- 3. Customer snapshot + banking details ----------
        if (req.customer is not null)
            await UpsertCustomerAsync(req.transactionId, req.customer, userId, now, ct);

        // ---------- 4. Beneficiaries (diff strategy) ----------
        await DiffSyncUdfChildrenAsync(
            existing:    await _db.BeneficiaryDetails.Where(b => b.TransactionId == req.transactionId).ToListAsync(ct),
            incoming:    req.beneficiaries,
            idSelector:  b => b.BeneficiaryDetailId,
            create:      udf => new BeneficiaryDetail
            {
                TenantId    = tx.TenantId,
                TransactionId = req.transactionId,
                UdfData     = udf,
                CreatedBy   = userId,
                CreatedDate = now
            },
            updateUdf:   (entity, udf) =>
            {
                entity.UdfData         = udf;
                entity.LastUpdatedBy   = userId;
                entity.LastUpdatedDate = now;
            },
            remove:      e => _db.BeneficiaryDetails.Remove(e),
            add:         e => _db.BeneficiaryDetails.Add(e));

        // ---------- 5. Stakeholders (diff strategy) ----------
        await DiffSyncUdfChildrenAsync(
            existing:    await _db.TransactionStakeholders.Where(s => s.TransactionId == req.transactionId).ToListAsync(ct),
            incoming:    req.stakeholders,
            idSelector:  s => s.TransactionStakeholderId,
            create:      udf => new TransactionStakeholder
            {
                TenantId      = tx.TenantId,
                TransactionId = req.transactionId,
                UdfData       = udf,
                CreatedBy     = userId,
                CreatedDate   = now
            },
            updateUdf:   (entity, udf) =>
            {
                entity.UdfData         = udf;
                entity.LastUpdatedBy   = userId;
                entity.LastUpdatedDate = now;
            },
            remove:      e => _db.TransactionStakeholders.Remove(e),
            add:         e => _db.TransactionStakeholders.Add(e));

        // ---------- 5.5 Flags (Slice 8) ----------
        // Stage flag inserts / updates and append-only history rows.
        // The differ stages everything onto the same DbContext so the
        // SaveChangesAsync below commits transaction header + UDF +
        // children + flag state + history atomically.
        await TransactionFlagDiffer.ApplyAsync(
            _db, req.transactionId, req.flags ?? new(), userId, now, ct);

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Transaction {TransactionId} updated by {UserId} — beneficiaries={BCount}, stakeholders={SCount}, flagChanges={FCount}.",
            req.transactionId, userId,
            req.beneficiaries?.Count ?? 0,
            req.stakeholders?.Count  ?? 0,
            req.flags?.Count         ?? 0);

        // 6. Return the canonical detail shape via the existing read handler.
        return await _mediator.Send(new GetTransactionByIdQuery(req.transactionId), ct);
    }

    // ============================================================
    // UDF helpers
    // ============================================================

    /// <summary>
    /// Serializes the parsed UDF object to nvarchar(max) JSON. Null input
    /// produces null output (the column is nullable). A non-null but empty
    /// object serializes to "{}".
    /// </summary>
    private static string? SerializeUdf(object? udf) =>
        udf is null ? null : JsonSerializer.Serialize(udf);

    private async Task UpsertTransactionDetailAsync(int transactionId, object? udfData, string userId, DateTime now, CancellationToken ct)
    {
        var detail = await _db.TransactionDetails.FirstOrDefaultAsync(d => d.TransactionId == transactionId, ct);
        var json   = SerializeUdf(udfData);

        if (detail is null)
        {
            // Brand-new detail row — INSERT.
            _db.TransactionDetails.Add(new TransactionDetail
            {
                TenantId      = 1,                         // single-tenant deployment; matches Slice 6 Step 3
                TransactionId = transactionId,
                UdfData       = json,
                CreatedBy     = userId,
                CreatedDate   = now
            });
        }
        else
        {
            // Existing — UPDATE in place.
            detail.UdfData         = json;
            detail.LastUpdatedBy   = userId;
            detail.LastUpdatedDate = now;
        }
    }

    private async Task UpsertCustomerAsync(int transactionId, UpdateCustomerInput cust, string userId, DateTime now, CancellationToken ct)
    {
        TransactionCustomerSnapshot? snapshot = null;

        if (cust.customerMasterId is > 0)
        {
            snapshot = await _db.TransactionCustomerSnapshots
                .FirstOrDefaultAsync(c => c.CustomerMasterId == cust.customerMasterId.Value && c.TransactionId == transactionId, ct);
        }
        snapshot ??= await _db.TransactionCustomerSnapshots
            .FirstOrDefaultAsync(c => c.TransactionId == transactionId, ct);

        var udfJson = SerializeUdf(cust.udfData);

        if (snapshot is null)
        {
            // No existing snapshot — INSERT new.
            snapshot = new TransactionCustomerSnapshot
            {
                TenantId                = 1,
                TransactionId           = transactionId,
                CustomerCode            = cust.customerCode,
                CustomerName            = cust.customerName,
                CustomerTitle           = cust.customerTitle,
                NationalIdentifierValue = cust.nationalIdentifierValue,
                NationalIdTypeLkp       = cust.nationalIdTypeLkp,
                CustomerTypeLkp         = cust.customerTypeLkp,
                CustomerSegmentLkp      = cust.customerSegmentLkp,
                CustomerSubSegmentLkp   = cust.customerSubSegmentLkp,
                LocationId              = cust.locationId,
                UdfData                 = udfJson,
                CreatedBy               = userId,
                CreatedDate             = now
            };
            _db.TransactionCustomerSnapshots.Add(snapshot);
            // Save so we have CustomerMasterId for the banking details.
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (cust.customerCode is not null)            snapshot.CustomerCode            = cust.customerCode;
            if (cust.customerName is not null)            snapshot.CustomerName            = cust.customerName;
            if (cust.customerTitle is not null)           snapshot.CustomerTitle           = cust.customerTitle;
            if (cust.nationalIdentifierValue is not null) snapshot.NationalIdentifierValue = cust.nationalIdentifierValue;
            if (cust.nationalIdTypeLkp.HasValue)          snapshot.NationalIdTypeLkp       = cust.nationalIdTypeLkp;
            if (cust.customerTypeLkp.HasValue)            snapshot.CustomerTypeLkp         = cust.customerTypeLkp;
            if (cust.customerSegmentLkp.HasValue)         snapshot.CustomerSegmentLkp      = cust.customerSegmentLkp;
            if (cust.customerSubSegmentLkp.HasValue)      snapshot.CustomerSubSegmentLkp   = cust.customerSubSegmentLkp;
            if (cust.locationId.HasValue)                 snapshot.LocationId              = cust.locationId;
            if (cust.udfData is not null)                 snapshot.UdfData                 = udfJson;
            snapshot.LastUpdatedBy   = userId;
            snapshot.LastUpdatedDate = now;
        }

        // Banking details — diff strategy nested under the customer snapshot.
        var existingBanks = await _db.CustomerBankingDetails
            .Where(b => b.CustomerMasterId == snapshot.CustomerMasterId)
            .ToListAsync(ct);

        DiffSync(
            existing:   existingBanks,
            incoming:   cust.bankingDetails,
            idSelector: b => b.CustomerBankingDetailId,
            keyOf:      i => i.customerBankingDetailId ?? 0,
            create:     input => new CustomerBankingDetail
            {
                TenantId            = 1,
                CustomerMasterId    = snapshot.CustomerMasterId,
                BankAccountNumber   = input.bankAccountNumber,
                BranchCode          = input.branchCode,
                BankCardNumber      = input.bankCardNumber,
                CardMemberName      = input.cardMemberName,
                AddressTypeLkpId    = input.addressTypeLkpId,
                InternetBanking     = input.internetBanking,
                InternetBankingTransactionAmount = input.internetBankingTransactionAmount,
                InternetAtmTransactionAmount     = input.internetAtmTransactionAmount,
                MailingCommunication = input.mailingCommunication,
                ChequeBookNumber    = input.chequeBookNumber,
                UdfData             = SerializeUdf(input.udfData),
                ActiveFlag          = true,
                EffectiveStartDate  = now,
                EffectiveEndDate    = now.AddYears(50),
                CreatedBy           = userId,
                CreatedDate         = now
            },
            updateExisting: (entity, input) =>
            {
                if (input.bankAccountNumber is not null) entity.BankAccountNumber = input.bankAccountNumber;
                if (input.branchCode is not null)        entity.BranchCode        = input.branchCode;
                if (input.bankCardNumber is not null)    entity.BankCardNumber    = input.bankCardNumber;
                if (input.cardMemberName is not null)    entity.CardMemberName    = input.cardMemberName;
                if (input.addressTypeLkpId.HasValue)     entity.AddressTypeLkpId  = input.addressTypeLkpId;
                if (input.internetBanking.HasValue)      entity.InternetBanking   = input.internetBanking;
                if (input.internetBankingTransactionAmount.HasValue) entity.InternetBankingTransactionAmount = input.internetBankingTransactionAmount;
                if (input.internetAtmTransactionAmount.HasValue)     entity.InternetAtmTransactionAmount     = input.internetAtmTransactionAmount;
                if (input.mailingCommunication is not null) entity.MailingCommunication = input.mailingCommunication;
                if (input.chequeBookNumber    is not null) entity.ChequeBookNumber    = input.chequeBookNumber;
                if (input.udfData is not null)             entity.UdfData             = SerializeUdf(input.udfData);
                entity.LastUpdatedBy   = userId;
                entity.LastUpdatedDate = now;
            },
            remove: e => _db.CustomerBankingDetails.Remove(e),
            add:    e => _db.CustomerBankingDetails.Add(e));
    }

    // ============================================================
    // Generic diff helpers
    // ============================================================

    /// <summary>
    /// Diff-syncs a collection of UDF-only children (beneficiaries,
    /// stakeholders) against the request. Rows with an <c>id</c> are
    /// UPDATEd; rows without an id are INSERTed; existing rows whose id
    /// is NOT in the request are DELETEd.
    /// </summary>
    private Task DiffSyncUdfChildrenAsync<TEntity>(
        List<TEntity>            existing,
        List<UdfChildInput>      incoming,
        Func<TEntity, int>       idSelector,
        Func<string?, TEntity>   create,
        Action<TEntity, string?> updateUdf,
        Action<TEntity>          remove,
        Action<TEntity>          add)
    {
        var incomingIds = incoming.Where(i => i.id is > 0).Select(i => i.id!.Value).ToHashSet();

        // DELETE: existing rows whose id isn't in incoming.
        foreach (var e in existing.Where(e => !incomingIds.Contains(idSelector(e))))
            remove(e);

        foreach (var input in incoming)
        {
            var udf = SerializeUdf(input.udfData);
            if (input.id is > 0)
            {
                // UPDATE: match the existing row, mutate udf.
                var match = existing.FirstOrDefault(e => idSelector(e) == input.id!.Value);
                if (match is not null) updateUdf(match, udf);
            }
            else
            {
                // INSERT.
                add(create(udf));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// More general diff helper — used for banking details where the
    /// input has many fields, not just udfData.
    /// </summary>
    private static void DiffSync<TEntity, TInput>(
        List<TEntity>             existing,
        List<TInput>              incoming,
        Func<TEntity, int>        idSelector,
        Func<TInput, int>         keyOf,
        Func<TInput, TEntity>     create,
        Action<TEntity, TInput>   updateExisting,
        Action<TEntity>           remove,
        Action<TEntity>           add)
    {
        var incomingIds = incoming.Select(keyOf).Where(k => k > 0).ToHashSet();

        foreach (var e in existing.Where(e => !incomingIds.Contains(idSelector(e))))
            remove(e);

        foreach (var input in incoming)
        {
            var key = keyOf(input);
            if (key > 0)
            {
                var match = existing.FirstOrDefault(e => idSelector(e) == key);
                if (match is not null) updateExisting(match, input);
            }
            else
            {
                add(create(input));
            }
        }
    }
}
