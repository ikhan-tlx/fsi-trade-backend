using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Users.ResetPassword;

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.userId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.newPassword)
            .NotEmpty().WithMessage("A new password is required.")
            .MinimumLength(6).MaximumLength(128);
    }
}
