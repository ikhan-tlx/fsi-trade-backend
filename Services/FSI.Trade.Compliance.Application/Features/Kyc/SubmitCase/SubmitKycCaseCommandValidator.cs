using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Kyc.SubmitCase;

public class SubmitKycCaseCommandValidator : AbstractValidator<SubmitKycCaseCommand>
{
    public SubmitKycCaseCommandValidator()
    {
        RuleFor(x => x.customerId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.transactionId).GreaterThan(0).When(x => x.transactionId.HasValue);
    }
}
