using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Workflow.GetCommands;

public class GetWorkflowCommandsQueryHandler
    : IRequestHandler<GetWorkflowCommandsQuery, IReadOnlyList<WorkflowCommand>>
{
    private readonly IWorkflowEngine     _engine;
    private readonly ICurrentUserService _current;

    public GetWorkflowCommandsQueryHandler(IWorkflowEngine engine, ICurrentUserService current)
    {
        _engine  = engine;
        _current = current;
    }

    public Task<IReadOnlyList<WorkflowCommand>> Handle(GetWorkflowCommandsQuery req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Available-commands lookup requires an authenticated caller.");
        return _engine.GetAvailableCommandsAsync(req.ProcessId, userId, ct);
    }
}
