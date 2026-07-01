using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Brains;

/// <summary>
/// Sandbox <see cref="IKycScreeningService"/> — returns a canned BRAINS v2.0
/// response so the UI can run KYC sanity while the localhost BRAINS service is
/// unavailable. Selected by DI when <c>Integration:Brains:Mode = Sandbox</c>;
/// start-up is blocked in Production (see Program.cs guard), so synthetic
/// screening data can never reach a prod environment.
///
/// Behaviour:
/// <list type="bullet">
///   <item>If <c>SandboxResponseFile</c> is set and readable, that canned JSON
///         is used.</item>
///   <item>Else if <c>SandboxForceStatus</c> is set, that STATUS is used
///         (or "fail" ⇒ TRANSOK=1).</item>
///   <item>Else a band is derived deterministically from the customer code so
///         QA can exercise PEP / High / Medium / Low across customers.</item>
/// </list>
/// Every call logs a clear SANDBOX warning so synthetic data is unmistakable.
/// </summary>
public class SandboxKycScreeningService : IKycScreeningService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] Bands = { "0", "1", "2", "3" }; // PEP / High / Medium / Low

    private readonly BrainsOptions                       _opt;
    private readonly IHostEnvironment                    _env;
    private readonly ILogger<SandboxKycScreeningService> _log;

    public SandboxKycScreeningService(
        IOptions<BrainsOptions>               opt,
        IHostEnvironment                      env,
        ILogger<SandboxKycScreeningService>   log)
    {
        _opt = opt.Value;
        _env = env;
        _log = log;
    }

    public Task<KycResult?> GetKycForCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var resp   = ResolveResponse(customerId);
        var result = BrainsResponseMapper.ToKycResult(resp);

        _log.LogWarning(
            "SANDBOX BRAINS — returning synthetic KYC for customer {Cust}: STATUS={Status}, TRANSOK={TransOk} → {Band}.",
            Mask(customerId), resp.Status, resp.TransOk, result?.RiskScore ?? "(none)");

        return Task.FromResult(result);
    }

    private BrainsKycResponse ResolveResponse(string customerId)
    {
        // 1. Canned file, if provided.
        if (!string.IsNullOrWhiteSpace(_opt.SandboxResponseFile))
        {
            try
            {
                var path = Path.IsPathRooted(_opt.SandboxResponseFile)
                    ? _opt.SandboxResponseFile
                    : Path.Combine(_env.ContentRootPath, _opt.SandboxResponseFile);

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var parsed = JsonSerializer.Deserialize<BrainsKycResponse>(json, JsonOpts);
                    if (parsed is not null) return parsed;
                }
                else
                {
                    _log.LogWarning("SANDBOX BRAINS — SandboxResponseFile '{Path}' not found; falling back to derived band.", path);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SANDBOX BRAINS — failed to read SandboxResponseFile; falling back to derived band.");
            }
        }

        // 2. Forced status, if provided.
        if (!string.IsNullOrWhiteSpace(_opt.SandboxForceStatus))
        {
            if (string.Equals(_opt.SandboxForceStatus, "fail", StringComparison.OrdinalIgnoreCase))
                return new BrainsKycResponse { Status = "", TransOk = "1", ErrorNo = "-1", ErrorMsg = "SANDBOX simulated failure" };

            // Legacy servlet shape: STATUS = "{riskCode}|{customerName}".
            return new BrainsKycResponse
            {
                Status   = $"{_opt.SandboxForceStatus!.Trim()}|{SandboxName(customerId)}",
                TransOk  = "0", ErrorNo = "", ErrorMsg = ""
            };
        }

        // 3. Deterministic band by customer code (stable per customer). Emit the
        //    legacy pipe-delimited "{riskCode}|{customerName}" STATUS so the mapper
        //    and downstream parity pipeline exercise the real contract.
        var bucket = (uint)(customerId?.GetHashCode() ?? 0) % (uint)Bands.Length;
        return new BrainsKycResponse
        {
            Status   = $"{Bands[bucket]}|{SandboxName(customerId)}",
            TransOk  = "0", ErrorNo = "", ErrorMsg = ""
        };
    }

    private static string SandboxName(string? customerId)
        => string.IsNullOrWhiteSpace(customerId) ? "SANDBOX CUSTOMER" : $"SANDBOX {customerId}";

    private static string Mask(string? id)
        => string.IsNullOrEmpty(id) ? "(none)"
         : id.Length <= 4 ? new string('*', id.Length)
         : new string('*', id.Length - 4) + id[^4..];
}
