using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Execute;

public class ExecuteWorkflowCommandHandler : IRequestHandler<ExecuteWorkflowCommand, WorkflowExecutionResult>
{
    private readonly IWorkflowEngine     _engine;
    private readonly ICurrentUserService _current;

    public ExecuteWorkflowCommandHandler(IWorkflowEngine engine, ICurrentUserService current)
    {
        _engine  = engine;
        _current = current;
    }

    public Task<WorkflowExecutionResult> Handle(ExecuteWorkflowCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Workflow execution requires an authenticated caller.");

        return _engine.ExecuteCommandAsync(
            processId:  req.processId,
            identityId: userId,
            command:    req.command,
            comments:   req.comments,
            parameters: req.parameters,
            ct:         ct);
    }
}
