using FSI.Trade.Compliance.Application.Contracts.Integrations;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.GetCaseStatus;

public record GetKycCaseStatusQuery(long RequestId) : IRequest<KycCaseStatusDto>;
