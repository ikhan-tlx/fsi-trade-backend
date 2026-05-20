using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Reports;
using FSI.Trade.Compliance.Application.Features.Reports.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Reports.RunHtml;

public class RunReportHtmlCommandHandler : IRequestHandler<RunReportHtmlCommand, RunReportHtmlResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IStoredProcedureRunner _spRunner;
    private readonly IReportHtmlRenderer   _renderer;

    public RunReportHtmlCommandHandler(
        IApplicationDbContext db,
        IStoredProcedureRunner spRunner,
        IReportHtmlRenderer renderer)
    {
        _db       = db;
        _spRunner = spRunner;
        _renderer = renderer;
    }

    public async Task<RunReportHtmlResult> Handle(RunReportHtmlCommand req, CancellationToken ct)
    {
        // Step 1 — allowlist gate.
        await ReportAllowlist.EnsureAllowedAsync(_db, req.StoredProcedure, ct);

        // Step 2 — template lookup. Match by TemplateName == ReportName.
        if (string.IsNullOrWhiteSpace(req.ReportName))
            throw new NotFoundException("report_template_not_found",
                "ReportName is required to locate the Liquid template.");

        var templateName = req.ReportName.Trim();
        var template = await _db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TemplateName == templateName, ct);

        if (template is null)
            throw new NotFoundException("report_template_not_found",
                $"No TmX_Template row exists for ReportName '{templateName}'.");

        // Step 3 — run the SP.
        var rows = await _spRunner.ExecuteAsync(
            req.StoredProcedure!,
            req.Arguments ?? new Dictionary<string, object?>(),
            commandTimeoutSeconds: null,
            ct: ct);

        // Step 4 — render Liquid → HTML. An empty template means the admin
        // hasn't put a body in yet; surface that distinctly so they can
        // notice and fix it instead of silently shipping a blank report.
        if (string.IsNullOrWhiteSpace(template.TemplateText))
            throw new NotFoundException("report_template_empty",
                $"Template '{templateName}' has no body configured (TmX_Template.Template_Text is empty).");

        var html = _renderer.Render(template.TemplateText!, rows);

        // Step 5 — return.
        return new RunReportHtmlResult
        {
            html              = html,
            reportName        = req.ReportName,
            reportVisibleName = req.ReportVisibleName,
            rowCount          = rows.Rows.Count
        };
    }
}
