using FluentValidation.Results;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Reports.Common;

/// <summary>
/// Validates that a requested stored-procedure name is on the report
/// allowlist before we let it anywhere near <c>IStoredProcedureRunner</c>.
///
/// The allowlist is the REPORT_TYPE rows in <c>TmX_Lookup</c>. Each row
/// represents one report the bank's admin has authorised; the SP name
/// is stored in either <c>HiddenValue</c> (preferred) or <c>LookupName</c>
/// (fallback for older rows). Matching is case-insensitive at the SQL
/// collation level.
///
/// Defence in depth: the FE only ever sends SP names it pulled out of
/// the same REPORT_TYPE lookup blob, but enforcing it server-side closes
/// the "what if I just POST a different SP name" hole. Failure surfaces
/// as a <see cref="ValidationException"/> → HTTP 400.
/// </summary>
public static class ReportAllowlist
{
    public const string ReportTypeLookup = "REPORT_TYPE";
    public const string StoredProcedureField = "StoredProcedure";

    public static async Task EnsureAllowedAsync(
        IApplicationDbContext db,
        string? storedProcedureName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storedProcedureName))
            throw new ValidationException(new[]
            {
                new ValidationFailure(StoredProcedureField, "Stored procedure name is required.")
            });

        var trimmed = storedProcedureName.Trim();

        var allowed = await db.Lookups
            .AsNoTracking()
            .Where(l => l.LookupType == ReportTypeLookup)
            .Where(l => l.HiddenValue == trimmed
                     || l.LookupName  == trimmed)
            .AnyAsync(ct);

        if (!allowed)
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    StoredProcedureField,
                    $"Stored procedure '{trimmed}' is not on the report allowlist (TmX_Lookup REPORT_TYPE).")
            });
    }
}
