using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Execute;

public class ExecuteWorkflowCommand : IRequest<WorkflowExecutionResult>
{
    public Guid                          processId  { get; set; }
    public string                        command    { get; set; } = "";
    public string?                       comments   { get; set; }
    public Dictionary<string, object?>?  parameters { get; set; }
}
