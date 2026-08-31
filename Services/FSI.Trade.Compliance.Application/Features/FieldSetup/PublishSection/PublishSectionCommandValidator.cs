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
            // Existing edits carry a real id; new inserts are keyed by tempId instead.
            item.RuleFor(i => i.tenantFieldSetupId)
                .GreaterThan(0).When(i => !i.isNew)
                .WithMessage("tenantFieldSetupId must be a positive integer.");

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

            // New-row (insert) rules.
            item.RuleFor(i => i.tempId)
                .NotNull().When(i => i.isNew)
                .WithMessage("A new item requires a tempId.");
            item.RuleFor(i => i.fieldTypeLkp)
                .NotNull().When(i => i.isNew)
                .WithMessage("A new item requires a fieldTypeLkp.");
            item.RuleFor(i => i.fieldLabel)
                .NotEmpty().When(i => i.isNew)
                .WithMessage("A new item requires a label.");
        });

        // A tab is required whenever the batch inserts new rows.
        RuleFor(x => x.tabId)
            .NotNull().When(x => x.items != null && x.items.Any(i => i.isNew))
            .WithMessage("tabId is required to add new fields.");

        // Existing rows may each appear at most once (by id); new rows at most once (by tempId).
        RuleFor(x => x.items)
            .Must(items =>
            {
                var edits = items.Where(i => !i.isNew).Select(i => i.tenantFieldSetupId).ToList();
                var news  = items.Where(i => i.isNew).Select(i => i.tempId).ToList();
                return edits.Distinct().Count() == edits.Count
                    && news.Distinct().Count() == news.Count;
            })
            .WithMessage("Each field (or new item) may appear at most once per publish.")
            .When(x => x.items != null && x.items.Count > 0);
    }
}
