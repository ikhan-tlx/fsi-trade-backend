using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Users.Create;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.userName)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(128);

        RuleFor(x => x.emailAddress).MaximumLength(256)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.emailAddress));

        RuleFor(x => x.firstName).MaximumLength(100);
        RuleFor(x => x.middleName).MaximumLength(100);
        RuleFor(x => x.lastName).MaximumLength(100);
        RuleFor(x => x.phoneNumber).MaximumLength(20);

        RuleFor(x => x.roleIds)
            .NotNull()
            .Must(ids => ids.Count <= 50)
            .WithMessage("Cannot assign more than 50 roles to a single user.");

        RuleForEach(x => x.roleIds).GreaterThan(0);
    }
}
