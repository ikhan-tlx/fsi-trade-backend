using FluentValidation;
using FSI.Trade.Compliance.Application.Features.FieldSetup.Update;

namespace FSI.Trade.Compliance.Application.Features.FieldSetup.PublishSection;

public class PublishSectionCommandValidator : AbstractValidator<PublishSectionCommand>
{
    public PublishSectionCommandValidator()
    {
        RuleFor(x => x.productId)
            .GreaterThan(0).WithMessage("productId must be a positive integer.");

        RuleFor(x => x.items)
            .NotNull().Must(i => i.Count > 0)
            .WithMessage("At least one item must be supplied.");

        RuleForEach(x => x.items).ChildRules(item =>
        {
            item.RuleFor(i => i.tenantFieldSetupId)
                .GreaterThan(0).WithMessage("tenantFieldSetupId must be a positive integer.");

            item.RuleFor(i => i.order)
                .GreaterThanOrEqualTo(0).WithMessage("order must be 0 or greater.");

            item.RuleFor(i => i.fieldLabel!)
                .MaximumLength(UpdateFieldSetupCommandValidator.MaxFieldLabelLength)
                .When(i => i.fieldLabel != null)
                .WithMessage($"fieldLabel cannot exceed {UpdateFieldSetupCommandValidator.MaxFieldLabelLength} characters.");

            item.RuleFor(i => i.placeholder!)
                .MaximumLength(UpdateFieldSetupCommandValidator.MaxPlaceholderLength)
                .When(i => i.placeholder != null)
                .WithMessage($"placeholder cannot exceed {UpdateFieldSetupCommandValidator.MaxPlaceholderLength} characters.");
        });

        // Each row may appear at most once in a batch.
        RuleFor(x => x.items)
            .Must(i => i.Select(x => x.tenantFieldSetupId).Distinct().Count() == i.Count)
            .WithMessage("Each field may appear at most once per publish.")
            .When(x => x.items != null && x.items.Count > 0);
    }
}
