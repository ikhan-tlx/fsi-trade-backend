using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Kyc.RiskAssessment;

/// <summary>
/// Builds the KYC Risk Assessment tab payload for a transaction: the customer's
/// full KYC execution history (BRAINS + FCCM + reconciled), with the execution
/// for THIS transaction flagged as current.
/// </summary>
public record GetKycRiskAssessmentQuery(long TransactionId) : IRequest<KycRiskAssessmentDto>;
