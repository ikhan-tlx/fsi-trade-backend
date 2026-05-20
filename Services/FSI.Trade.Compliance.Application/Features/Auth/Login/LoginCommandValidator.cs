using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Auth.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.username).NotEmpty().MaximumLength(256);
        RuleFor(x => x.password).NotEmpty();
    }
}
