using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Disable;

public class DisableTwoFactorCommandValidator : AbstractValidator<DisableTwoFactorCommand>
{
    public DisableTwoFactorCommandValidator()
    {
        RuleFor(x => x.UserId)         .NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
    }
}
