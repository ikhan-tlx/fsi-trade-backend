using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

public class AddFlagScopeCommandValidator : AbstractValidator<AddFlagScopeCommand>
{
    public AddFlagScopeCommandValidator()
    {
        RuleFor(x => x.flagId)
            .GreaterThan(0);

        // productId is OPTIONAL — null means an "all products" (global) scope.
        // When supplied it must be positive.
        RuleFor(x => x.productId!.Value)
            .GreaterThan(0)
            .When(x => x.productId.HasValue)
            .WithMessage("Product ID must be a positive value when supplied.");

        // A tab-level scope cannot exist without a product (mirrors the
        // CK_TmX_Flag_Scope_TabRequiresProduct DB constraint).
        RuleFor(x => x.productId)
            .NotNull()
            .When(x => x.tabId.HasValue)
            .WithMessage("A tab-level scope requires a product.");
    }
}
