using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Users.Update;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.userId).NotEmpty().MaximumLength(100);

        RuleFor(x => x.emailAddress).MaximumLength(256)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.emailAddress));

        RuleFor(x => x.firstName).MaximumLength(100);
        RuleFor(x => x.middleName).MaximumLength(100);
        RuleFor(x => x.lastName).MaximumLength(100);
        RuleFor(x => x.phoneNumber).MaximumLength(20);

        // roleIds is optional on update — null means "don't touch the mapping".
        // Empty array means "remove all roles". Non-null with values means
        // "replace with this set".
        When(x => x.roleIds is not null, () =>
        {
            RuleFor(x => x.roleIds!)
                .Must(ids => ids.Count <= 50)
                .WithMessage("Cannot assign more than 50 roles to a single user.");
            RuleForEach(x => x.roleIds!).GreaterThan(0);
        });
    }
}
