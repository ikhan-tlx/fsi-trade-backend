using FluentValidation;

namespace FSI.Trade.Compliance.Application.Features.Reports.GeneratePdf;

public class GeneratePdfFromHtmlCommandValidator : AbstractValidator<GeneratePdfFromHtmlCommand>
{
    public GeneratePdfFromHtmlCommandValidator()
    {
        RuleFor(x => x.HTML)
            .NotEmpty()
            .WithMessage("HTML body is required for PDF generation.");
    }
}
