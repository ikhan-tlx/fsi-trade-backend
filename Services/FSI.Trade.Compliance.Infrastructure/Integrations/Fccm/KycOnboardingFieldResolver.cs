using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// The legacy <c>OnboardingCustomer</c> fields that are DERIVED from KYC form
/// data (everything beyond the static identity/classification constants). All
/// strings; null = not present / not resolvable (the payload omits nulls).
/// </summary>
public sealed class KycDerivedFields
{
    public string? CountryOfResidence         { get; set; }
    public string? EmploymentType             { get; set; }
    public string? CountryOfBirth             { get; set; }
    public string? WealthSource               { get; set; }
    public string? EmployerName               { get; set; }
    public string? Industry                   { get; set; }
    public string? CountryOfIncorporation     { get; set; }
    public string? CitizenshipStatus          { get; set; }
    public string? PrimaryCitizenship         { get; set; }
    public string? SecondaryCitizenship       { get; set; }
    public string? OrganizationLegalStructure { get; set; }
    public string? CustomerNAICSCode          { get; set; }
    public string? TypeofRequest              { get; set; }
    public string? RelationshipTeamCode       { get; set; }
    public string? Occupation                 { get; set; }
    public string? OrganizationName           { get; set; }
    public string? NumberOfDependents         { get; set; }
    public string? JobTitle                   { get; set; }
    public string? Age                        { get; set; }
    public string? RetirementYear             { get; set; }
    public string? EmploymentStatus           { get; set; }
    public string? OverImp                    { get; set; }   // OVER_IMP
    public string? DefSect                    { get; set; }   // DEF_SECT
    public string? StructBus                  { get; set; }
    public string? CustPerf                   { get; set; }   // CUST_PERF
    public string? Alias                      { get; set; }
    public string? HsCode                     { get; set; }   // HS_CD
}

/// <summary>
/// Ports the legacy <c>KYCRequestModel.SetRealtimeValues</c> risk-parameter
/// derivation (ICBC_Integration_Test) onto the new schema. The KYC UDF storage
/// is byte-identical between systems:
/// <list type="bullet">
///   <item><c>TmX_Transaction_Detail.udf_data</c> — single <c>KycParam*</c>
///         values (some raw, some Lookup_IDs).</item>
///   <item><c>TmX_Customer_Master.udf_data</c> — multi-value grids
///         (<c>TmxKYCSPOR</c>, <c>TmxKYCTPFAC</c>, …), each row
///         <c>{ KycParamN : Lookup_ID }</c>.</item>
///   <item><c>TmX_Lookup.Hidden_Value</c> — resolves a Lookup_ID to its code.</item>
/// </list>
/// Three derivation mechanisms (matching legacy):
///   1. DIRECT — raw <c>KycParam*</c> value off the transaction detail.
///   2. SINGLE HIDDEN — detail <c>KycParam*</c> is a Lookup_ID → Hidden_Value.
///   3. MULTI MAX-SCORE — collect a grid's Lookup_IDs → Hidden_Values → the
///      Oracle <c>FCC_OB_RISK_PRMS_CODE_SCRE_MAP</c> highest-SCORE CODE_VALUE
///      (<see cref="FccmOracleReader.GetMaxScoreCodeValueAsync"/>).
///   + HS_CD — first <c>TmxHsCodeList</c> row's <c>KycParam8</c> Hidden_Value.
///
/// Best-effort: any failure logs and returns whatever resolved so far — the
/// onboarding submit must never break because a derivation lookup failed.
/// </summary>
public sealed class KycOnboardingFieldResolver
{
    private readonly IApplicationDbContext _db;
    private readonly FccmOracleReader      _oracle;
    private readonly ILogger<KycOnboardingFieldResolver> _log;

    public KycOnboardingFieldResolver(
        IApplicationDbContext db,
        FccmOracleReader      oracle,
        ILogger<KycOnboardingFieldResolver> log)
    {
        _db     = db;
        _oracle = oracle;
        _log    = log;
    }

    public async Task<KycDerivedFields> ResolveAsync(int transactionId, CancellationToken ct = default)
    {
        var r = new KycDerivedFields();
        try
        {
            var detailUdf = await _db.TransactionDetails.AsNoTracking()
                .Where(d => d.TransactionId == transactionId)
                .Select(d => d.UdfData).FirstOrDefaultAsync(ct);
            var custUdf = await _db.TransactionCustomerSnapshots.AsNoTracking()
                .Where(c => c.TransactionId == transactionId)
                .Select(c => c.UdfData).FirstOrDefaultAsync(ct);

            using var detailDoc = TryParse(detailUdf);
            using var custDoc   = TryParse(custUdf);
            if (detailDoc is null && custDoc is null) return r;   // nothing captured

            JsonElement? detail = detailDoc?.RootElement;
            JsonElement? cust   = custDoc?.RootElement;

            // ── Bulk-resolve every Lookup_ID we will need from TmX_Lookup ──
            var neededIds = new HashSet<int>();
            foreach (var k in SingleHiddenKeys) AddId(neededIds, detail, k);
            foreach (var (grid, param) in GridParams) AddGridIds(neededIds, cust, grid, param);
            AddGridIds(neededIds, cust, "TmxHsCodeList", "KycParam8");
            var hidden = await LoadHiddenValuesAsync(neededIds, ct);

            // ── 1. DIRECT (raw transaction-detail values) ──
            r.RelationshipTeamCode = Raw(detail, "KycParam4");
            r.Occupation           = Raw(detail, "KycParam13");
            r.OrganizationName     = Raw(detail, "KycParam14");
            r.NumberOfDependents   = Raw(detail, "KycParam15");
            r.RetirementYear       = Raw(detail, "KycParam16");
            r.JobTitle             = Raw(detail, "KycParam17");
            r.Age                  = Raw(detail, "KycParam18");
            r.EmploymentStatus     = Raw(detail, "KycParam21");
            r.OverImp              = Raw(detail, "KycParam22");
            r.Alias                = Raw(detail, "KycParam3");

            // ── 2. SINGLE HIDDEN (detail Lookup_ID → Hidden_Value) ──
            r.CitizenshipStatus    = HiddenOf(hidden, detail, "KycParam34");
            r.DefSect              = HiddenOf(hidden, detail, "KycParam10");
            r.StructBus            = HiddenOf(hidden, detail, "KycParam28");
            r.CustPerf             = HiddenOf(hidden, detail, "KycParam20");
            r.SecondaryCitizenship = HiddenOf(hidden, detail, "KycParam1");

            // ── 3. MULTI MAX-SCORE (grid → Hidden_Values → Oracle max SCORE) ──
            r.CountryOfResidence         = await MaxScoreAsync(hidden, cust, "TmxKYCSPOR", "KycParam29", ct);
            r.EmploymentType             = await MaxScoreAsync(hidden, cust, "TmxKYCTPFAC", "KycParam5",  ct);
            r.CountryOfBirth             = await MaxScoreAsync(hidden, cust, "TmxKYCSPSC", "KycParam2",   ct);
            r.WealthSource               = await MaxScoreAsync(hidden, cust, "TmxKYCDCITC", "KycParam76", ct);
            r.EmployerName               = await MaxScoreAsync(hidden, cust, "TmxKYCTOP",  "KycParam31",  ct);
            r.Industry                   = await MaxScoreAsync(hidden, cust, "TmxKYCLCPG", "KycParam6",   ct);
            r.CountryOfIncorporation     = await MaxScoreAsync(hidden, cust, "TmxKYCFCPG", "KycParam7",   ct);
            r.PrimaryCitizenship         = await MaxScoreAsync(hidden, cust, "TmxKYCCOMG", "KycParam9",   ct);
            r.OrganizationLegalStructure = await MaxScoreAsync(hidden, cust, "TmxKYCIPL",  "KycParam38",  ct);
            r.CustomerNAICSCode          = await MaxScoreAsync(hidden, cust, "TmxKYCIPD",  "KycParam39",  ct);
            r.TypeofRequest              = await MaxScoreAsync(hidden, cust, "TmxKYCMOS",  "KycParam12",  ct);

            // ── HS_CD (first HS-code grid row's Hidden_Value) ──
            var hsIds = GridIds(cust, "TmxHsCodeList", "KycParam8");
            if (hsIds.Count > 0 && hidden.TryGetValue(hsIds[0], out var hs)) r.HsCode = hs;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KYC risk-param derivation failed for transaction {TxnId}; returning partial result.", transactionId);
        }
        return r;
    }

    // Detail keys that are Lookup_IDs resolved to a single Hidden_Value.
    private static readonly string[] SingleHiddenKeys =
        { "KycParam34", "KycParam10", "KycParam28", "KycParam20", "KycParam1" };

    // Grid table → the KycParam key inside each row (multi-value max-score).
    private static readonly (string Grid, string Param)[] GridParams =
    {
        ("TmxKYCSPOR", "KycParam29"), ("TmxKYCTPFAC", "KycParam5"), ("TmxKYCSPSC", "KycParam2"),
        ("TmxKYCDCITC", "KycParam76"), ("TmxKYCTOP", "KycParam31"), ("TmxKYCLCPG", "KycParam6"),
        ("TmxKYCFCPG", "KycParam7"), ("TmxKYCCOMG", "KycParam9"), ("TmxKYCIPL", "KycParam38"),
        ("TmxKYCIPD", "KycParam39"), ("TmxKYCMOS", "KycParam12"),
    };

    private async Task<string?> MaxScoreAsync(
        IReadOnlyDictionary<int, string?> hidden, JsonElement? cust, string grid, string param, CancellationToken ct)
    {
        var codes = GridIds(cust, grid, param)
            .Select(id => hidden.TryGetValue(id, out var v) ? v : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
        if (codes.Count == 0) return null;
        return await _oracle.GetMaxScoreCodeValueAsync(codes, ct);
    }

    private async Task<IReadOnlyDictionary<int, string?>> LoadHiddenValuesAsync(HashSet<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string?>();
        var rows = await _db.Lookups.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.HiddenValue })
            .ToListAsync(ct);
        var d = new Dictionary<int, string?>();
        foreach (var row in rows) d[row.Id] = row.HiddenValue;
        return d;
    }

    private static string? HiddenOf(IReadOnlyDictionary<int, string?> hidden, JsonElement? detail, string key)
        => TryGetId(detail, key) is int id && hidden.TryGetValue(id, out var v) ? v : null;

    private static void AddId(HashSet<int> set, JsonElement? obj, string key)
    {
        if (TryGetId(obj, key) is int id) set.Add(id);
    }

    private static void AddGridIds(HashSet<int> set, JsonElement? cust, string grid, string param)
    {
        foreach (var id in GridIds(cust, grid, param)) set.Add(id);
    }

    private static List<int> GridIds(JsonElement? cust, string grid, string param)
    {
        var list = new List<int>();
        if (cust is not { ValueKind: JsonValueKind.Object }) return list;
        if (!cust.Value.TryGetProperty(grid, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(param, out var v)
                && TryToInt(v) is int id) list.Add(id);
        }
        return list;
    }

    /// <summary>Raw scalar value of a detail key as a string (number or string).</summary>
    private static string? Raw(JsonElement? obj, string key)
    {
        if (obj is not { ValueKind: JsonValueKind.Object } || !obj.Value.TryGetProperty(key, out var el))
            return null;
        var s = el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l.ToString() : el.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? TryGetId(JsonElement? obj, string key)
        => obj is { ValueKind: JsonValueKind.Object } && obj.Value.TryGetProperty(key, out var el)
            ? TryToInt(el) : null;

    private static int? TryToInt(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i : null,
        JsonValueKind.String => int.TryParse(el.GetString(), out var i) ? i : null,
        _ => null,
    };

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }
}
