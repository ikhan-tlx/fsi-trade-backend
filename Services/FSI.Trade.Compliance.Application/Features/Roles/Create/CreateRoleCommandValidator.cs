using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Roles.Create;

public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.roleName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.roleDescription)
            .MaximumLength(200);
    }
}
