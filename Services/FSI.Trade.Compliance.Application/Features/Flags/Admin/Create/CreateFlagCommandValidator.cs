using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Create;

public class CreateFlagCommandValidator : AbstractValidator<CreateFlagCommand>
{
    public CreateFlagCommandValidator()
    {
        RuleFor(x => x.flagName)
            .NotEmpty().WithMessage("Flag name is required.")
            .MaximumLength(200);

        RuleFor(x => x.flagDescription)
            .NotEmpty().WithMessage("Flag description is required.");

        RuleFor(x => x.flagTypeLkpId)
            .GreaterThan(0)
            .WithMessage("Flag type lookup ID is required (FLAG_TYPE: Manual / Automated).");

        // Auto-generated when blank; if supplied, enforce shape + length.
        RuleFor(x => x.flagCode!)
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9._-]+$")
                .WithMessage("Flag code may contain only letters, digits, dot, underscore, hyphen.")
            .When(x => !string.IsNullOrWhiteSpace(x.flagCode));

        RuleFor(x => x.defaultWeight!.Value)
            .InclusiveBetween(0m, 999.99m)
            .WithMessage("Default weight must be between 0 and 999.99.")
            .When(x => x.defaultWeight.HasValue);

        RuleFor(x => x.sourceSystem!)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.sourceSystem));
    }
}
