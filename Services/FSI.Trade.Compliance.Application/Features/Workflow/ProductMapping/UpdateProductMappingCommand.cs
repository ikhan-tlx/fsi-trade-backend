using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Workflow.ProductMapping;

public class UpdateProductMappingCommand : IRequest<Unit>
{
    public string     schemeCode  { get; set; } = "";
    public List<int>  productIds  { get; set; } = new();
}

public class UpdateProductMappingCommandValidator : AbstractValidator<UpdateProductMappingCommand>
{
    public UpdateProductMappingCommandValidator()
    {
        RuleFor(x => x.schemeCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.productIds).NotNull().Must(ids => ids.Count <= 500)
            .WithMessage("Cannot map more than 500 products to a single scheme.");
        RuleForEach(x => x.productIds).GreaterThan(0);
    }
}

/// <summary>
/// Rewrites the product → scheme mapping for the supplied scheme code.
/// Semantics mirror the legacy WorkflowController.PostProductMapping:
///
/// <list type="number">
///   <item>Every product currently sitting in this scheme has its
///         <c>Workflow_Scheme_Code</c> cleared.</item>
///   <item>Every product ID in <paramref name="productIds"/> is re-pointed
///         at this scheme.</item>
/// </list>
///
/// Orchestrated entirely in the Application layer over EF Core — no vendor
/// types touched. Audit fields (<c>LastUpdatedBy</c>/<c>LastUpdatedDate</c>)
/// are updated on every row we mutate so the legacy audit trail stays intact.
/// </summary>
public class UpdateProductMappingCommandHandler : IRequestHandler<UpdateProductMappingCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateProductMappingCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(UpdateProductMappingCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Product mapping requires an authenticated caller.");

        var schemeCode  = req.schemeCode.Trim();
        var distinctIds = req.productIds.Where(i => i > 0).Distinct().ToHashSet();
        var now         = DateTime.UtcNow;

        // Pull (1) every product already on this scheme, plus (2) every
        // product the caller wants to put on this scheme, in a single round
        // trip. The set-difference logic happens in memory.
        var affected = await _db.Products
            .Where(p => p.WorkflowSchemeCode == schemeCode || distinctIds.Contains(p.ProductId))
            .ToListAsync(ct);

        foreach (var p in affected)
        {
            var shouldBeOnScheme = distinctIds.Contains(p.ProductId);
            var newCode          = shouldBeOnScheme ? schemeCode : null;

            if (p.WorkflowSchemeCode == newCode)
                continue;       // nothing to do — already correct

            p.WorkflowSchemeCode = newCode;
            p.LastUpdatedBy      = userId;
            p.LastUpdatedDate    = now;
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
