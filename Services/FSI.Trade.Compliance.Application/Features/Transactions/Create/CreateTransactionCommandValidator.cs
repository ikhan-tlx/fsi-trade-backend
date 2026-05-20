using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Create;

public class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionCommandValidator()
    {
        RuleFor(x => x.productId)
            .GreaterThan(0).WithMessage("productId is required.");

        RuleFor(x => x.companyBranchId)
            .GreaterThan(0).WithMessage("companyBranchId is required.");

        RuleFor(x => x.clientReferenceNumber)
            .MaximumLength(100);

        RuleFor(x => x.customer).NotNull();

        When(x => x.customer is not null, () =>
        {
            RuleFor(x => x.customer.customerCode)
                .NotEmpty().WithMessage("customer.customerCode is required.")
                .MaximumLength(50);

            RuleFor(x => x.customer.customerName)
                .MaximumLength(100);

            RuleFor(x => x.customer.nationalIdentifierValue)
                .MaximumLength(100);
        });
    }
}
