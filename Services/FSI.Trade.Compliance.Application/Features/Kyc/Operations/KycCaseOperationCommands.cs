using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.Operations;

// Privileged (Kyc.Operate) operational actions for the Risk Assessment tab.
// Thin MediatR commands over IKycCaseOperationsService, mirroring the
// GetCaseStatus/SubmitCase slices (handler → service).

public record RetryKycCaseCommand(long RequestId)   : IRequest<KycCaseSubmissionResult>;
public record RepollKycCaseCommand(long RequestId)  : IRequest<KycCaseStatusDto>;
public record RefreshKycCaseCommand(long RequestId) : IRequest<KycCaseStatusDto>;

public class RetryKycCaseCommandHandler : IRequestHandler<RetryKycCaseCommand, KycCaseSubmissionResult>
{
    private readonly IKycCaseOperationsService _ops;
    public RetryKycCaseCommandHandler(IKycCaseOperationsService ops) => _ops = ops;
    public Task<KycCaseSubmissionResult> Handle(RetryKycCaseCommand req, CancellationToken ct)
        => _ops.RetryAsync(req.RequestId, ct);
}

public class RepollKycCaseCommandHandler : IRequestHandler<RepollKycCaseCommand, KycCaseStatusDto>
{
    private readonly IKycCaseOperationsService _ops;
    public RepollKycCaseCommandHandler(IKycCaseOperationsService ops) => _ops = ops;
    public Task<KycCaseStatusDto> Handle(RepollKycCaseCommand req, CancellationToken ct)
        => _ops.RepollAsync(req.RequestId, ct);
}

public class RefreshKycCaseCommandHandler : IRequestHandler<RefreshKycCaseCommand, KycCaseStatusDto>
{
    private readonly IKycCaseOperationsService _ops;
    public RefreshKycCaseCommandHandler(IKycCaseOperationsService ops) => _ops = ops;
    public Task<KycCaseStatusDto> Handle(RefreshKycCaseCommand req, CancellationToken ct)
        => _ops.RefreshAsync(req.RequestId, ct);
}
