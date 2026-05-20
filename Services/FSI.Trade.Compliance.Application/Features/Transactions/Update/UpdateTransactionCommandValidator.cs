using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Update;

public class UpdateTransactionCommandValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionCommandValidator()
    {
        RuleFor(x => x.transactionId)
            .GreaterThan(0).WithMessage("transactionId is required (path parameter).");

        RuleFor(x => x.clientReferenceNumber).MaximumLength(100);

        When(x => x.customer is not null, () =>
        {
            RuleFor(x => x.customer!.customerCode).MaximumLength(50);
            RuleFor(x => x.customer!.customerName).MaximumLength(100);
            RuleFor(x => x.customer!.nationalIdentifierValue).MaximumLength(100);

            RuleForEach(x => x.customer!.bankingDetails).ChildRules(b =>
            {
                b.RuleFor(x => x.bankAccountNumber).MaximumLength(100);
                b.RuleFor(x => x.branchCode).MaximumLength(50);
                b.RuleFor(x => x.bankCardNumber).MaximumLength(100);
                b.RuleFor(x => x.cardMemberName).MaximumLength(150);
            });
        });

        RuleFor(x => x.beneficiaries).NotNull();
        RuleFor(x => x.stakeholders).NotNull();
    }
}
