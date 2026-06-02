using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.FieldSetup.Update;

public class UpdateFieldSetupCommandValidator : AbstractValidator<UpdateFieldSetupCommand>
{
    /// <summary>Matches TmX_Tenant_Field_Setup.Field_Label column length (NVARCHAR(MAX) in DB, capped here for sanity).</summary>
    public const int MaxFieldLabelLength = 500;

    /// <summary>Matches the Placeholder column added by migration 2026_05_029.</summary>
    public const int MaxPlaceholderLength = 500;

    public UpdateFieldSetupCommandValidator()
    {
        RuleFor(x => x.tenantFieldSetupId)
            .GreaterThan(0).WithMessage("tenantFieldSetupId must be a positive integer.");

        // Reject a no-op PUT — at least one mutable field must be present.
        RuleFor(x => x)
            .Must(x => x.fieldLabel  != null
                   ||  x.placeholder != null
                   ||  x.isActive    != null
                   ||  x.isMandatory != null)
            .WithMessage("At least one of fieldLabel / placeholder / isActive / isMandatory must be provided.");

        RuleFor(x => x.fieldLabel!)
            .MaximumLength(MaxFieldLabelLength)
                .WithMessage($"fieldLabel cannot exceed {MaxFieldLabelLength} characters.")
            .When(x => x.fieldLabel != null);

        RuleFor(x => x.placeholder!)
            .MaximumLength(MaxPlaceholderLength)
                .WithMessage($"placeholder cannot exceed {MaxPlaceholderLength} characters.")
            .When(x => x.placeholder != null);
    }
}
