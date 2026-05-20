using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Confirm;

public class ConfirmTwoFactorCommandValidator : AbstractValidator<ConfirmTwoFactorCommand>
{
    public ConfirmTwoFactorCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Otp)   .NotEmpty().Matches(@"^\d{6}$").WithMessage("OTP must be 6 digits.");
    }
}
