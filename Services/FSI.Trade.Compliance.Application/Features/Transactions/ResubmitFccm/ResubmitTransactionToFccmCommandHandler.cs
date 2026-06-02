using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Features.Transactions.ResubmitFccm;

public class ResubmitTransactionToFccmCommandHandler
    : IRequestHandler<ResubmitTransactionToFccmCommand, ResubmitTransactionToFccmResult>
{
    private readonly IFccmTradeCaseService _fccmTrade;
    private readonly ILogger<ResubmitTransactionToFccmCommandHandler> _log;

    public ResubmitTransactionToFccmCommandHandler(
        IFccmTradeCaseService fccmTrade,
        ILogger<ResubmitTransactionToFccmCommandHandler> log)
    {
        _fccmTrade = fccmTrade;
        _log       = log;
    }

    public async Task<ResubmitTransactionToFccmResult> Handle(
        ResubmitTransactionToFccmCommand req, CancellationToken ct)
    {
        _log.LogWarning(
            "Manual FCCM resubmit requested for TxId={TxId}",
            req.transactionId);

        var result = await _fccmTrade.ResubmitAsync(req.transactionId, ct);

        return new ResubmitTransactionToFccmResult
        {
            transactionId  = result.TransactionId,
            fccmCaseId     = result.FccmCaseId,
            flagEventCount = result.FlagEventCount,
            submittedAt    = result.SubmittedAt,
        };
    }
}
