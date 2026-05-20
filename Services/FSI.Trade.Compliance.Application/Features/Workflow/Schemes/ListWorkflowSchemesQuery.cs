using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Schemes;

public record ListWorkflowSchemesQuery : IRequest<IReadOnlyList<WorkflowScheme>>;

/// <summary>
/// Lists every non-obsolete workflow scheme registered with the engine.
/// Orchestrates the read entirely in the Application layer — pulls directly
/// from <c>WorkflowProcessScheme</c> via EF Core. No vendor types touched,
/// no <see cref="IWorkflowEngine"/> roundtrip.
///
/// v21 NOTE: scheme code column is named <c>SchemeCode</c>. A single
/// SchemeCode can have multiple rows (one per definition revision); we
/// collapse to distinct codes and return the first non-obsolete revision's
/// SchemeCode as the Display.
/// </summary>
public class ListWorkflowSchemesQueryHandler : IRequestHandler<ListWorkflowSchemesQuery, IReadOnlyList<WorkflowScheme>>
{
    private readonly IApplicationDbContext _db;

    public ListWorkflowSchemesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowScheme>> Handle(ListWorkflowSchemesQuery req, CancellationToken ct)
    {
        var rows = await _db.WorkflowProcessSchemes
            .AsNoTracking()
            .Where(s => !s.IsObsolete && s.SchemeCode != null && s.SchemeCode != "")
            .Select(s => s.SchemeCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return rows.Select(c => new WorkflowScheme
        {
            Code    = c,
            Display = c                    // Slice 5: scheme XML has no friendly-name column we trust;
                                           // re-use code. Slice 6 may parse the XML <Process Name="..."/>
                                           // attribute to surface a display name.
        }).ToList();
    }
}
