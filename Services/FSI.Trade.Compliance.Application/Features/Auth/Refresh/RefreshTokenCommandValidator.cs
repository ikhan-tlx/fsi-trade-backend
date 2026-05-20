using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Auth.Refresh;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
        => RuleFor(x => x.refresh_token).NotEmpty().MinimumLength(10);
}
