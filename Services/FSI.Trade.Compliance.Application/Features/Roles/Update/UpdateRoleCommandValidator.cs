using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Roles.Update;

public class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.roleId).GreaterThan(0);
        RuleFor(x => x.roleName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.roleDescription).MaximumLength(200);
    }
}
