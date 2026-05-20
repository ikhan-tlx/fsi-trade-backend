using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Reports;
using FSI.Trade.Compliance.Application.Features.Reports.Common;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Reports.RunExcel;

public class RunReportExcelCommandHandler
    : IRequestHandler<RunReportExcelCommand, RunReportExcelResult>
{
    private readonly IApplicationDbContext   _db;
    private readonly IStoredProcedureRunner  _spRunner;
    private readonly IReportExcelExporter    _excel;

    public RunReportExcelCommandHandler(
        IApplicationDbContext db,
        IStoredProcedureRunner spRunner,
        IReportExcelExporter excel)
    {
        _db       = db;
        _spRunner = spRunner;
        _excel    = excel;
    }

    public async Task<RunReportExcelResult> Handle(RunReportExcelCommand req, CancellationToken ct)
    {
        await ReportAllowlist.EnsureAllowedAsync(_db, req.StoredProcedure, ct);

        var rows = await _spRunner.ExecuteAsync(
            req.StoredProcedure!,
            req.Arguments ?? new Dictionary<string, object?>(),
            commandTimeoutSeconds: null,
            ct: ct);

        var visibleName = string.IsNullOrWhiteSpace(req.ReportVisibleName)
            ? req.ReportName ?? "Report"
            : req.ReportVisibleName!;

        var bytes = _excel.Export(rows, visibleName);

        return new RunReportExcelResult
        {
            ExcelBytes        = bytes,
            ReportVisibleName = visibleName,
            RowCount          = rows.Rows.Count
        };
    }
}
