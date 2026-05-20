using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Auth.ResetExpiredPassword;

public class ResetExpiredPasswordCommandValidator : AbstractValidator<ResetExpiredPasswordCommand>
{
    public ResetExpiredPasswordCommandValidator()
    {
        RuleFor(x => x.Username)        .NotEmpty().MaximumLength(256);
        RuleFor(x => x.CurrentPassword) .NotEmpty();
        RuleFor(x => x.NewPassword)     .NotEmpty().MinimumLength(6);
        RuleFor(x => x).Must(x => x.CurrentPassword != x.NewPassword)
            .WithMessage("New password must differ from the current password.");
    }
}
