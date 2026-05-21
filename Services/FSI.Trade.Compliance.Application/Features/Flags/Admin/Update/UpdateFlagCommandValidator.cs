using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Update;

public class UpdateFlagCommandValidator : AbstractValidator<UpdateFlagCommand>
{
    public UpdateFlagCommandValidator()
    {
        RuleFor(x => x.flagId)
            .GreaterThan(0);

        RuleFor(x => x.flagName)
            .NotEmpty().WithMessage("Flag name is required.")
            .MaximumLength(200);

        RuleFor(x => x.flagDescription)
            .NotEmpty().WithMessage("Flag description is required.");

        RuleFor(x => x.flagTypeLkpId)
            .GreaterThan(0)
            .WithMessage("Flag type lookup ID is required (FLAG_TYPE).");

        RuleFor(x => x.defaultWeight!.Value)
            .InclusiveBetween(0m, 999.99m)
            .When(x => x.defaultWeight.HasValue);

        RuleFor(x => x.sourceSystem!)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.sourceSystem));
    }
}
