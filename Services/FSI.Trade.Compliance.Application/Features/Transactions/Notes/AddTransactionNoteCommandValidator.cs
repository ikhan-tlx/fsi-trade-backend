using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Notes;

/// <summary>
/// FluentValidation rules for the add-note request. Picked up automatically
/// by the MediatR <c>ValidationBehaviour</c> pipeline — no manual wiring.
/// </summary>
public class AddTransactionNoteCommandValidator : AbstractValidator<AddTransactionNoteCommand>
{
    /// <summary>Max length matches TmX_Application_Remark.Comments column (nvarchar(MAX) — capped at a sane API limit).</summary>
    public const int MaxCommentLength = 4000;

    public AddTransactionNoteCommandValidator()
    {
        RuleFor(x => x.transactionId)
            .GreaterThan(0).WithMessage("transactionId must be a positive integer.");

        RuleFor(x => x.comments)
            .NotNull().WithMessage("comments is required.")
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("comments cannot be empty or whitespace.")
            .Must(c => (c ?? "").Trim().Length <= MaxCommentLength)
                .WithMessage($"comments cannot exceed {MaxCommentLength} characters.");

        // actionType is optional — only sanity-check length when present.
        RuleFor(x => x.actionType)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.actionType))
            .WithMessage("actionType cannot exceed 50 characters.");
    }
}
