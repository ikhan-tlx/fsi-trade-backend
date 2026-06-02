using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Application;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Notes;

/// <summary>
/// Inserts a <c>TmX_Application_Remark</c> row. Module_Code stays at "5"
/// (transaction-scope) to match the read side. Remarks_Lkp is 0 — the
/// legacy schema requires the column but our notes endpoint doesn't
/// categorise via that lookup (the FE doesn't surface it either).
/// </summary>
public class AddTransactionNoteCommandHandler
    : IRequestHandler<AddTransactionNoteCommand, AddTransactionNoteResult>
{
    /// <summary>Module_Code value identifying "Transaction" rows — matches the read handler.</summary>
    public const string TransactionModuleCode = "5";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public AddTransactionNoteCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<AddTransactionNoteResult> Handle(
        AddTransactionNoteCommand req,
        CancellationToken         ct)
    {
        // Shape validation (comments non-empty, length cap) lives in
        // AddTransactionNoteCommandValidator and runs via the MediatR
        // ValidationBehaviour before we get here.
        var comments = req.comments!.Trim();

        // Validate transaction exists (cheap exists check; the row count
        // is bounded so AnyAsync is fine).
        var txExists = await _db.Transactions.AsNoTracking()
            .AnyAsync(t => t.TransactionId == req.transactionId, ct);
        if (!txExists)
            throw new NotFoundException(
                "transaction_not_found",
                $"Transaction {req.transactionId} does not exist.");

        var userId = _current.UserId
            ?? throw new AuthenticationException(
                "unauthenticated",
                "Adding a note requires an authenticated caller.");

        // Resolve a display name for CreatedBy. Convention used across the
        // rest of the backend: CreatedBy = username, UserId = User_ID (Guid-shaped).
        var userName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(ct);

        var entity = new ApplicationRemark
        {
            TransactionId = req.transactionId,
            ModuleCode    = TransactionModuleCode,
            TenantId      = _current.TenantId ?? 1,
            ActionType    = string.IsNullOrWhiteSpace(req.actionType) ? "Note" : req.actionType!.Trim(),
            RemarksLkp    = 0,
            Comments      = comments,
            UserId        = userId,
            CreatedBy     = userName ?? userId,
            CreatedDate   = DateTime.UtcNow
        };

        _db.ApplicationRemarks.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new AddTransactionNoteResult
        {
            applicationRemarkId = entity.ApplicationRemarkId,
            comments            = entity.Comments,
            actionType          = entity.ActionType,
            userId              = entity.UserId,
            createdDate         = entity.CreatedDate,
            createdBy           = entity.CreatedBy
        };
    }
}
