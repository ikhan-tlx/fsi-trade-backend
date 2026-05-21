using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

public class AddFlagScopeCommandValidator : AbstractValidator<AddFlagScopeCommand>
{
    public AddFlagScopeCommandValidator()
    {
        RuleFor(x => x.flagId)
            .GreaterThan(0);

        RuleFor(x => x.productId)
            .GreaterThan(0)
            .WithMessage("Product ID is required.");
    }
}
