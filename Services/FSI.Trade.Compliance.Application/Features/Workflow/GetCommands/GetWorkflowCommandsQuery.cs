using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Workflow.GetCommands;

public record GetWorkflowCommandsQuery(Guid ProcessId) : IRequest<IReadOnlyList<WorkflowCommand>>;
