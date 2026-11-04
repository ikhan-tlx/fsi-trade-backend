using FSI.Trade.Compliance.Application.Contracts.Flags;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using FSI.Trade.Compliance.Domain.Entities.Reference;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using FSI.Trade.Compliance.Infrastructure.Flags;
using FSI.Trade.Compliance.Infrastructure.Flags.Metrics;
using FSI.Trade.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// Auto-flag engine behaviour around the UAT report: a field mapped to the
/// "High Risk Countries" reference list with RaisePerMatch ("separate") ON.
///   • per-field fan-out works even when the fields carry the SAME value;
///   • the engine reads the IN-FLIGHT UDF (not a stale committed snapshot), so a
///     single save raises every flag — it is no longer "one save behind".
/// </summary>
public class AutoFlagEvaluatorTests
{
    private const int FlagId = 100;
    private const int TxnId  = 500;
    private const string ListCode = "HighRiskCountries";

    private static AutoFlagEvaluator Evaluator(ApplicationDbContext db)
        => new(db, NullLogger<AutoFlagEvaluator>.Instance,
               new MetricHandlerRegistry(Array.Empty<IRuleMetricHandler>()),
               new FakeRuleDataProvider());

    // ---- seeding ------------------------------------------------------------

    /// <summary>Seed the flag + the HIGH-RISK reference list (IR/KP/SY) + a txn.
    /// One <see cref="FlagRule"/> per field code, each in its own Rule_Group (OR).</summary>
    private static void Seed(ApplicationDbContext db, bool perMatch, params string[] fieldCodes)
    {
        db.FlagCatalogues.Add(new FlagCatalogue
        {
            FlagId = FlagId, FlagCode = "ARL-HRC", FlagName = "High-risk country",
            FlagDescription = "country in high risk list", FlagTypeLkpId = 1,
            RaisePerMatch = perMatch, ActiveFlag = true, Status = "Active",
            CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
        });
        int rid = 1, grp = 1;
        foreach (var fc in fieldCodes)
        {
            db.FlagRules.Add(new FlagRule
            {
                RuleId = rid++, FlagId = FlagId, RuleGroup = grp++, EvaluationOrder = 1,
                LeftSource = "Trade", LeftFieldCode = fc,
                Operator = "In", RightOperandType = "ReferenceList",
                ReferenceListCode = ListCode, ActiveFlag = true,
                CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
            });
        }
        db.FlagScopes.Add(new FlagScope
        { FlagScopeId = 1, FlagId = FlagId, ProductId = null, TabId = null, ActiveFlag = true, CreatedBy = "seed", CreatedDate = DateTime.UtcNow });

        db.ReferenceTypes.Add(new ReferenceType
        { ReferenceTypeId = 1, TypeCode = "HIGHRISKCOUNTRIES", TypeName = "High Risk Countries", ActiveFlag = true, CreatedDate = DateTime.UtcNow });
        long vid = 1;
        foreach (var code in new[] { "IR", "KP", "SY" })
        {
            db.ReferenceValues.Add(new ReferenceValue
            { ReferenceValueId = vid, Code = code, Name = code, ActiveFlag = true, CreatedDate = DateTime.UtcNow });
            db.ReferenceValueTypeMaps.Add(new ReferenceValueTypeMap
            { MapId = vid, ReferenceValueId = vid, ReferenceTypeId = 1, ActiveFlag = true, CreatedDate = DateTime.UtcNow });
            vid++;
        }

        db.Transactions.Add(new Transaction
        { TransactionId = TxnId, TenantId = 1, ProductId = 3, TransactionDate = DateTime.UtcNow, CreatedDate = DateTime.UtcNow });
    }

    private static void SeedDetail(ApplicationDbContext db, string udfJson)
        => db.TransactionDetails.Add(new TransactionDetail
        { TransactionDetailId = 1, TransactionId = TxnId, TenantId = 1, UdfData = udfJson, CreatedBy = "seed", CreatedDate = DateTime.UtcNow });

    private static string FlatUdf(params (string field, string value)[] pairs)
        => "{" + string.Join(",", pairs.Select(p => $"\"{p.field}\":\"{p.value}\"")) + "}";

    private static string Grid(string field, params string[] values)
        => "{\"Countries\":[" + string.Join(",", values.Select(v => $"{{\"{field}\":\"{v}\"}}")) + "]}";

    // ---- per-field fan-out --------------------------------------------------

    [Fact]
    public async Task ThreeDistinctFields_SameValue_PerMatch_RaisesThree()
    {
        await using var db = TestSupport.NewDb();
        Seed(db, perMatch: true, "CTRY_A", "CTRY_B", "CTRY_C");
        SeedDetail(db, FlatUdf(("CTRY_A", "IR"), ("CTRY_B", "IR"), ("CTRY_C", "IR")));
        await db.SaveChangesAsync();

        var res = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);
        Assert.Equal(3, res.MatchedValues.Count);   // one flag PER FIELD, same value
    }

    [Fact]
    public async Task ThreeDistinctFields_DifferentValues_PerMatch_RaisesThree()
    {
        await using var db = TestSupport.NewDb();
        Seed(db, perMatch: true, "CTRY_A", "CTRY_B", "CTRY_C");
        SeedDetail(db, FlatUdf(("CTRY_A", "IR"), ("CTRY_B", "KP"), ("CTRY_C", "SY")));
        await db.SaveChangesAsync();

        var res = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);
        Assert.Equal(3, res.MatchedValues.Count);
    }

    [Fact]
    public async Task OneGridField_DistinctValues_PerMatch_RaisesPerValue()
    {
        await using var db = TestSupport.NewDb();
        Seed(db, perMatch: true, "HR_COUNTRY");
        SeedDetail(db, Grid("HR_COUNTRY", "IR", "KP", "SY"));
        await db.SaveChangesAsync();

        var res = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);
        Assert.Equal(3, res.MatchedValues.Count);
    }

    [Fact]
    public async Task Aggregate_ManyMatches_RaisesSingleSummary()
    {
        await using var db = TestSupport.NewDb();
        Seed(db, perMatch: false, "CTRY_A", "CTRY_B", "CTRY_C");
        SeedDetail(db, FlatUdf(("CTRY_A", "IR"), ("CTRY_B", "KP"), ("CTRY_C", "SY")));
        await db.SaveChangesAsync();

        var res = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);
        Assert.True(res.WouldRaise);
        Assert.Single(res.MatchedValues);           // one aggregate summary row
    }

    // ---- the stale-read fix (symptom #2: "edit+save brings the rest") -------

    [Fact]
    public async Task Evaluate_ReadsInFlightUdf_NotStaleCommittedSnapshot()
    {
        await using var db = TestSupport.NewDb();
        Seed(db, perMatch: true, "CTRY_A", "CTRY_B", "CTRY_C");
        SeedDetail(db, FlatUdf(("CTRY_A", "IR")));   // prior committed state: ONE field set
        await db.SaveChangesAsync();

        // Mimic UpdateTransactionCommandHandler: mutate the tracked detail, do NOT save,
        // then evaluate (the handler evaluates BEFORE its SaveChanges).
        var detail = await db.TransactionDetails.FirstAsync(d => d.TransactionId == TxnId);
        detail.UdfData = FlatUdf(("CTRY_A", "IR"), ("CTRY_B", "KP"), ("CTRY_C", "SY"));

        var beforeSave = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);
        await db.SaveChangesAsync();
        var afterSave  = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);

        // The whole point of the fix: the first (pre-commit) evaluation already sees the
        // in-flight edit, so it is NOT one save behind.
        Assert.Equal(3, beforeSave.MatchedValues.Count);
        Assert.Equal(afterSave.MatchedValues.Count, beforeSave.MatchedValues.Count);
    }

    [Fact]
    public async Task Evaluate_NewlyInsertedDetail_SeenOnFirstSave()
    {
        // The real UAT case: the transaction exists (created by the wizard) but its
        // detail row is being INSERTED on the first form-save. The detail is an Added
        // entity not yet in the DB — the engine must still see it via the ChangeTracker.
        await using var db = TestSupport.NewDb();
        Seed(db, perMatch: true, "CTRY_A", "CTRY_B", "CTRY_C");   // seeds + saves the Transaction
        await db.SaveChangesAsync();

        // Mimic UpsertTransactionDetailAsync inserting a brand-new detail (NOT saved yet).
        db.TransactionDetails.Add(new TransactionDetail
        {
            TransactionDetailId = 1, TransactionId = TxnId, TenantId = 1,
            UdfData = FlatUdf(("CTRY_A", "IR"), ("CTRY_B", "KP"), ("CTRY_C", "SY")),
            CreatedBy = "seed", CreatedDate = DateTime.UtcNow,
        });

        var res = await Evaluator(db).DryRunAsync(FlagId, TxnId, default);
        Assert.Equal(3, res.MatchedValues.Count);   // flags on the FIRST save, not the second
    }
}

internal sealed class FakeRuleDataProvider : IRuleDataProvider
{
    public Task<string?> ResolveCustomerCodeAsync(int transactionId, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<CustomerTxnRow>> GetCustomerWindowAsync(string customerCode, DateTime fromInclusive, DateTime toInclusive, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CustomerTxnRow>>(Array.Empty<CustomerTxnRow>());
    public Task<DateTime?> GetLastActivityDateBeforeAsync(string customerCode, DateTime beforeExclusive, int excludeTransactionId, CancellationToken ct) => Task.FromResult<DateTime?>(null);
    public Task<IReadOnlyList<string>> GetAmountFieldCodesAsync(int productId, int? tabId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<string?> GetLatestKycUdfAsync(string customerCode, CancellationToken ct) => Task.FromResult<string?>(null);
}
