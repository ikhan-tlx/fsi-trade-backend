using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Flags;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.AutoFlags;

/// <summary>Shared writer for a flag's scope rows. Priority: explicit `scopes`
/// (product + sub-product tab) → legacy `scopeProductIds` (product-level) → global.
/// One TmX_Flag_Scope row per entry; dedupes on (Product_ID, Tab_ID).</summary>
internal static class AutoFlagScopeWriter
{
    public static void Write(IApplicationDbContext db, int flagId,
        List<ScopeEntryDto>? scopes, List<int>? productIds, string user, DateTime now)
    {
        var order = 0;
        if (scopes is { Count: > 0 })
        {
            foreach (var key in scopes.Select(s => (s.productId, s.tabId)).Distinct())
                db.FlagScopes.Add(new FlagScope
                {
                    FlagId = flagId, ProductId = key.productId, TabId = key.tabId,
                    SortOrder = order++, ActiveFlag = true, CreatedBy = user, CreatedDate = now,
                });
            return;
        }
        if (productIds is { Count: > 0 })
        {
            foreach (var pid in productIds.Distinct())
                db.FlagScopes.Add(new FlagScope
                {
                    FlagId = flagId, ProductId = pid, TabId = null,
                    SortOrder = order++, ActiveFlag = true, CreatedBy = user, CreatedDate = now,
                });
            return;
        }
        // Nothing specified → applies to all products (global).
        db.FlagScopes.Add(new FlagScope
        {
            FlagId = flagId, ProductId = null, TabId = null,
            SortOrder = 0, ActiveFlag = true, CreatedBy = user, CreatedDate = now,
        });
    }
}

/// <summary>Shared writer for a flag's rule rows — used by BOTH Create and Update so the two
/// paths can never drift. (A prior drift persisted rules on update WITHOUT Handler_Key /
/// Right_Factor / Right_Factor_Unit, silently blanking the metric on every aggregate/variance
/// or ×/% flag that was edited.) Also guards completeness: an Aggregate/Derived left operand is
/// meaningless without its Handler_Key + Aggregate_Spec, so a half-built rule is rejected rather
/// than saved in a state the evaluator can't run.</summary>
internal static class AutoFlagRuleWriter
{
    /// <summary>Completeness guard, run BEFORE any DB mutation so a rejected save can never leave
    /// a flag rule-less: an Aggregate/Derived left operand is meaningless without its metric
    /// (Handler_Key + Aggregate_Spec), so a half-built rule is refused rather than persisted in a
    /// state the evaluator can't run.</summary>
    public static void Validate(List<AutoFlagRuleDto>? rules)
    {
        if (rules is null) return;
        foreach (var r in rules)
        {
            var src = string.IsNullOrWhiteSpace(r.leftSource) ? "Trade" : r.leftSource;
            if ((src == "Aggregate" || src == "Derived")
                && (string.IsNullOrWhiteSpace(r.handlerKey) || string.IsNullOrWhiteSpace(r.aggregateSpec)))
                throw new ConflictException("auto_flag_incomplete_metric",
                    $"A condition (group {(r.ruleGroup <= 0 ? 1 : r.ruleGroup)}) uses a metric/aggregate left operand but no metric is selected. Choose an aggregate/metric — or pick a field — before saving.");
        }
    }

    public static void Write(IApplicationDbContext db, int flagId,
        List<AutoFlagRuleDto>? rules, string user, DateTime now)
    {
        if (rules is null) return;
        foreach (var r in rules)
            db.FlagRules.Add(new FlagRule
            {
                FlagId           = flagId,
                RuleGroup        = r.ruleGroup <= 0 ? 1 : r.ruleGroup,
                EvaluationOrder  = r.evaluationOrder <= 0 ? 1 : r.evaluationOrder,
                LogicalOperator  = string.IsNullOrWhiteSpace(r.logicalOperator) ? "AND" : r.logicalOperator,
                LeftSource       = string.IsNullOrWhiteSpace(r.leftSource) ? "Trade" : r.leftSource,
                LeftFieldCode    = r.leftFieldCode,
                Operator         = r.@operator,
                RightOperandType = r.rightOperandType,
                RightSource      = r.rightSource,
                RightFieldCode   = r.rightFieldCode,
                StaticValue      = r.staticValue,
                ReferenceListCode= r.referenceListCode,
                AggregateSpec    = r.aggregateSpec,
                HandlerKey       = r.handlerKey,
                RightFactor      = r.rightFactor,
                RightFactorUnit  = r.rightFactorUnit,
                ActiveFlag       = true,
                CreatedBy        = user,
                CreatedDate      = now,
            });
    }
}

// ===================== Create ==========================================
/// <summary>POST /api/v1/AutoFlag — create an auto flag (Draft) + its rules + scope.</summary>
public class CreateAutoFlagCommand : AutoFlagWriteModel, IRequest<int> { }

public class CreateAutoFlagCommandHandler : IRequestHandler<CreateAutoFlagCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public CreateAutoFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<int> Handle(CreateAutoFlagCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.name))
            throw new ConflictException("auto_flag_name_required", "An auto flag name is required.");
        AutoFlagRuleWriter.Validate(req.rules);

        var user = _current.UserName ?? _current.UserId ?? "system";
        var now  = DateTime.UtcNow;

        var flag = new FlagCatalogue
        {
            // Auto Red-flag code: "ARL-" + short random alphanumeric (mirrors the
            // legacy "MRL" manual code style). Caller may still supply an explicit code.
            FlagCode        = string.IsNullOrWhiteSpace(req.flagCode)
                                ? $"ARL-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
                                : req.flagCode!.Trim(),
            FlagName        = req.name!.Trim(),
            FlagDescription = req.description?.Trim() ?? req.name!.Trim(),
            FlagTypeLkpId   = await AutoFlagLookup.AutoTypeIdAsync(_db, ct),
            SeverityLkpId   = await AutoFlagLookup.SeverityIdAsync(_db, req.severity ?? "Medium", ct),
            DefaultWeight   = 1.00m,
            RaisePerMatch   = req.raisePerMatch ?? false,
            ActiveFlag      = false,   // Draft until approved
            IsBuiltIn       = false,
            IsDeleted       = false,
            Status          = "Draft",
            VersionNo       = 1,
            CreatedBy       = user,
            CreatedDate     = now,
        };
        _db.FlagCatalogues.Add(flag);
        await _db.SaveChangesAsync(ct);   // get FlagId

        AutoFlagScopeWriter.Write(_db, flag.FlagId, req.scopes, req.scopeProductIds, user, now);
        AutoFlagRuleWriter.Write(_db, flag.FlagId, req.rules, user, now);
        await _db.SaveChangesAsync(ct);
        return flag.FlagId;
    }
}

// ===================== Update ==========================================
/// <summary>PUT /api/v1/AutoFlag/{id} — replace metadata, scope and rules.</summary>
public class UpdateAutoFlagCommand : AutoFlagWriteModel, IRequest<bool>
{
    public int FlagId { get; set; }
}

public class UpdateAutoFlagCommandHandler : IRequestHandler<UpdateAutoFlagCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public UpdateAutoFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<bool> Handle(UpdateAutoFlagCommand req, CancellationToken ct)
    {
        var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == req.FlagId && !f.IsDeleted, ct)
                   ?? throw new NotFoundException("auto_flag_not_found", $"Auto flag '{req.FlagId}' not found.");

        // Reject a half-built aggregate/derived rule up front — BEFORE we replace scopes/rules —
        // so a rejected update can never leave the flag rule-less.
        AutoFlagRuleWriter.Validate(req.rules);

        var user = _current.UserName ?? _current.UserId ?? "system";
        var now  = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(req.name))        flag.FlagName        = req.name!.Trim();
        if (req.description != null)                     flag.FlagDescription = req.description.Trim();
        if (!string.IsNullOrWhiteSpace(req.severity))    flag.SeverityLkpId   = await AutoFlagLookup.SeverityIdAsync(_db, req.severity, ct);
        if (req.raisePerMatch.HasValue)                  flag.RaisePerMatch   = req.raisePerMatch.Value;
        flag.LastUpdatedBy   = user;
        flag.LastUpdatedDate = now;

        // Replace scopes + rules (config-version semantics: latest wins).
        var oldScopes = await _db.FlagScopes.Where(s => s.FlagId == flag.FlagId).ToListAsync(ct);
        _db.FlagScopes.RemoveRange(oldScopes);
        var oldRules = await _db.FlagRules.Where(r => r.FlagId == flag.FlagId).ToListAsync(ct);
        _db.FlagRules.RemoveRange(oldRules);
        await _db.SaveChangesAsync(ct);

        AutoFlagScopeWriter.Write(_db, flag.FlagId, req.scopes, req.scopeProductIds, user, now);

        AutoFlagRuleWriter.Write(_db, flag.FlagId, req.rules, user, now);

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ===================== Lifecycle (maker-checker, minimal) ==============
/// <summary>POST /api/v1/AutoFlag/{id}/SubmitForApproval — marks the flag ready for a checker.
/// (Full Draft→Pending→Active state needs the lifecycle columns; P1 keeps it inactive.)</summary>
public record SubmitAutoFlagCommand(int FlagId) : IRequest<bool>;

public class SubmitAutoFlagCommandHandler : IRequestHandler<SubmitAutoFlagCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public SubmitAutoFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<bool> Handle(SubmitAutoFlagCommand req, CancellationToken ct)
    {
        var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == req.FlagId && !f.IsDeleted, ct)
                   ?? throw new NotFoundException("auto_flag_not_found", $"Auto flag '{req.FlagId}' not found.");
        var u = _current.UserName ?? _current.UserId ?? "system";
        var now = DateTime.UtcNow;
        flag.Status          = "PendingApproval";
        flag.SubmittedBy     = u;
        flag.SubmittedDate   = now;
        flag.LastUpdatedBy   = u;
        flag.LastUpdatedDate = now;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>POST /api/v1/AutoFlag/{id}/Approve — checker activates the flag (it starts evaluating).</summary>
public record ApproveAutoFlagCommand(int FlagId) : IRequest<bool>;

public class ApproveAutoFlagCommandHandler : IRequestHandler<ApproveAutoFlagCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public ApproveAutoFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<bool> Handle(ApproveAutoFlagCommand req, CancellationToken ct)
    {
        var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == req.FlagId && !f.IsDeleted, ct)
                   ?? throw new NotFoundException("auto_flag_not_found", $"Auto flag '{req.FlagId}' not found.");
        var u = _current.UserName ?? _current.UserId ?? "system";
        var now = DateTime.UtcNow;
        flag.ActiveFlag      = true;
        flag.Status          = "Active";
        flag.ApprovedBy      = u;
        flag.ApprovedDate    = now;
        flag.VersionNo       = (flag.VersionNo ?? 1);
        flag.LastUpdatedBy   = u;
        flag.LastUpdatedDate = now;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>POST /api/v1/AutoFlag/{id}/Retire — checker deactivates an active flag (stops evaluating).</summary>
public record RetireAutoFlagCommand(int FlagId) : IRequest<bool>;

public class RetireAutoFlagCommandHandler : IRequestHandler<RetireAutoFlagCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public RetireAutoFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<bool> Handle(RetireAutoFlagCommand req, CancellationToken ct)
    {
        var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == req.FlagId && !f.IsDeleted, ct)
                   ?? throw new NotFoundException("auto_flag_not_found", $"Auto flag '{req.FlagId}' not found.");
        flag.ActiveFlag      = false;
        flag.Status          = "Retired";
        flag.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "system";
        flag.LastUpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>DELETE /api/v1/AutoFlag/{id} — soft-delete a draft/retired auto flag (built-ins blocked).</summary>
public record DeleteAutoFlagCommand(int FlagId) : IRequest<bool>;

public class DeleteAutoFlagCommandHandler : IRequestHandler<DeleteAutoFlagCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public DeleteAutoFlagCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<bool> Handle(DeleteAutoFlagCommand req, CancellationToken ct)
    {
        var flag = await _db.FlagCatalogues.FirstOrDefaultAsync(f => f.FlagId == req.FlagId && !f.IsDeleted, ct)
                   ?? throw new NotFoundException("auto_flag_not_found", $"Auto flag '{req.FlagId}' not found.");
        if (flag.IsBuiltIn)
            throw new ConflictException("auto_flag_builtin", "Built-in flags can't be deleted — retire it instead.");
        var u = _current.UserName ?? _current.UserId ?? "system";
        flag.IsDeleted   = true;
        flag.DeletedBy   = u;
        flag.DeletedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ===================== Test (dry-run) ==================================
/// <summary>POST /api/v1/AutoFlag/{id}/Test — dry-run the flag's rules against a transaction.</summary>
public record TestAutoFlagCommand(int FlagId, string TransactionId) : IRequest<AutoFlagTestResultDto>;

public class TestAutoFlagCommandHandler : IRequestHandler<TestAutoFlagCommand, AutoFlagTestResultDto>
{
    private readonly IAutoFlagEvaluator _engine;
    private readonly IApplicationDbContext _db;
    public TestAutoFlagCommandHandler(IAutoFlagEvaluator engine, IApplicationDbContext db)
    { _engine = engine; _db = db; }

    public async Task<AutoFlagTestResultDto> Handle(TestAutoFlagCommand req, CancellationToken ct)
    {
        var raw = (req.TransactionId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return new AutoFlagTestResultDto { wouldRaise = false, detail = "Enter a transaction number." };

        // Authors enter the business transaction NUMBER (e.g. TLX00200001012), not the
        // internal id. Resolve it; fall back to treating a bare integer as the id.
        int? txnId = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionNumber == raw)
            .Select(t => (int?)t.TransactionId)
            .FirstOrDefaultAsync(ct);
        if (txnId is null && int.TryParse(raw, out var asId)
            && await _db.Transactions.AsNoTracking().AnyAsync(t => t.TransactionId == asId, ct))
            txnId = asId;
        if (txnId is null)
            return new AutoFlagTestResultDto { wouldRaise = false, detail = $"No transaction found with number '{raw}'." };

        var dr = await _engine.DryRunAsync(req.FlagId, txnId.Value, ct);
        return new AutoFlagTestResultDto
        {
            wouldRaise    = dr.WouldRaise,
            matchedRules  = dr.MatchedRules,
            detail        = dr.Detail,
            perMatch      = dr.PerMatch,
            matchedValues = dr.MatchedValues,
            proof         = dr.Proof,
        };
    }
}
