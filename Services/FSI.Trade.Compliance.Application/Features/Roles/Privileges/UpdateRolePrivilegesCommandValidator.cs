using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Roles.Privileges;

public class UpdateRolePrivilegesCommandValidator : AbstractValidator<UpdateRolePrivilegesCommand>
{
    public UpdateRolePrivilegesCommandValidator()
    {
        RuleFor(x => x.roleId).GreaterThan(0);

        RuleFor(x => x.privilegeIds)
            .NotNull()
            .Must(ids => ids.Count <= 500)
            .WithMessage("Cannot grant more than 500 privileges to a single role.");

        RuleForEach(x => x.privilegeIds)
            .GreaterThan(0)
            .WithMessage("Privilege IDs must be positive integers.");
    }
}
