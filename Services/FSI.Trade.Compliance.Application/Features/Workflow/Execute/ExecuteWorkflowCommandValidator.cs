using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Execute;

public class ExecuteWorkflowCommandValidator : AbstractValidator<ExecuteWorkflowCommand>
{
    public ExecuteWorkflowCommandValidator()
    {
        RuleFor(x => x.processId).NotEqual(Guid.Empty);
        RuleFor(x => x.command)  .NotEmpty().MaximumLength(100);
        RuleFor(x => x.comments) .MaximumLength(1000);
    }
}
