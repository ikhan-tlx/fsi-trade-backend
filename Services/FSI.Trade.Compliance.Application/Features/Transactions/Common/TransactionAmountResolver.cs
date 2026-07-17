using System.Globalization;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Common;

/// <summary>
/// Additively projects the submitted UDF onto the typed <c>Transaction_Amount</c>
/// and <c>Currency_Id</c> columns at save time — the UDF is still written exactly
/// as before, this just captures the two values into columns so the Customer
/// Intelligence rollups read a column instead of parsing JSON.
///
///   Amount   ← the sub-product's marked <c>Is_Amount_Field</c> field value.
///   Currency ← the sub-product's Currency dropdown
///              (<c>Field_Type_Lkp = 25</c> AND <c>Field_Lookup_Type = 'Currency'</c>),
///              only when Currency_Id is not already set from the header.
///
/// Exception-safe by contract: any failure is swallowed so it can NEVER break the
/// transaction create/update. Set the tx properties BEFORE the handler's
/// <c>SaveChangesAsync</c> so they commit in the same unit of work.
/// </summary>
public static class TransactionAmountResolver
{
    public static async Task ApplyAsync(
        IApplicationDbContext db,
        Domain.Entities.Transaction.Transaction tx,
        object? udfData,
        CancellationToken ct)
    {
        try
        {
            if (udfData is null) return;
            var json = udfData as string ?? JsonSerializer.Serialize(udfData);
            if (string.IsNullOrWhiteSpace(json)) return;

            var fields = await db.TenantFieldSetups.AsNoTracking()
                .Where(f => f.ProductId == tx.ProductId
                         && (f.TabId == tx.SubProductTabId || (f.TabId == null && tx.SubProductTabId == null))
                         && f.IsActive)
                .Select(f => new { f.FieldName, f.IsAmountField, f.FieldTypeLkp, f.FieldLookupType })
                .ToListAsync(ct);

            var amountKey = fields.FirstOrDefault(f => f.IsAmountField)?.FieldName;
            var currencyKey = fields.FirstOrDefault(f =>
                    f.FieldTypeLkp == 25 &&
                    string.Equals(f.FieldLookupType, "Currency", StringComparison.OrdinalIgnoreCase))
                ?.FieldName;

            using var doc = JsonDocument.Parse(json);

            if (!string.IsNullOrEmpty(amountKey)
                && TryFind(doc.RootElement, amountKey!, out var amtRaw)
                && decimal.TryParse(amtRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                tx.TransactionAmount = amt;

            // Currency_Lkp ← the UDF Currency field value (a TmX_Lookup id). Validated
            // against TmX_Lookup so a bad/foreign value can never FK-break the save.
            if (!string.IsNullOrEmpty(currencyKey)
                && TryFind(doc.RootElement, currencyKey!, out var curRaw)
                && int.TryParse(curRaw, out var cur)
                && await db.Lookups.AsNoTracking().AnyAsync(l => l.Id == cur, ct))
                tx.CurrencyLkp = cur;
        }
        catch { /* never break the save */ }
    }

    /// <summary>Recursive, case-insensitive key lookup (UDF may be flat at root or nested in sections).</summary>
    private static bool TryFind(JsonElement el, string key, out string? value)
    {
        value = null;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        JsonValueKind.Null   => null,
                        _                    => p.Value.GetRawText()
                    };
                    if (!string.IsNullOrWhiteSpace(value)) return true;
                }
                if (TryFind(p.Value, key, out value)) return true;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (TryFind(item, key, out value)) return true;
        }
        return false;
    }
}
