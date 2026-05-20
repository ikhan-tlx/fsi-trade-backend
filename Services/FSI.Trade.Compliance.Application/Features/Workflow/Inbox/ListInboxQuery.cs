using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Inbox;

public class ListInboxQuery : PagedQuery, IRequest<PagedResult<WorkflowInboxItem>>
{
}
