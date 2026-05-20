using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Auth.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OldPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
        RuleFor(x => x).Must(x => x.OldPassword != x.NewPassword)
            .WithMessage("New password must differ from the old password.");
    }
}
