using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Read;

/// <summary>
/// Read projections for transaction flags. Two distinct concerns:
///   • <see cref="LoadSetForTransactionAsync"/> — the flags actually SET on a
///     transaction (rows in <c>TmX_Transaction_Flag</c>). Drives
///     <c>GET /Transaction/{id}</c> and <c>GET /Transaction/{id}/Flags</c>.
///   • <see cref="LoadByProductAsync"/> — the selectable flag catalogue scoped
///     by product (+ optional sub-product tab), with no transaction context.
///     Drives the flag-picker endpoints <c>GET /Flag/Product/{id}</c> and
///     <c>GET /Flag/Product/{id}/Tab/{tab}</c>.
///
/// Keeping these separate is deliberate: a transaction "carries" only the flags
/// an analyst picked, while the picker shows everything applicable to the
/// product/sub-product. Mixing them is what made the detail endpoint return the
/// entire scope catalogue.
/// </summary>
public static class TransactionFlagProjection
{
    /// <summary>
    /// The flags actually SET on a transaction — i.e. the rows that exist in
    /// <c>TmX_Transaction_Flag</c> for it (one row per selected flag), NOT the
    /// full applicable scope catalogue. This is what <c>GET /Transaction/{id}</c>
    /// and <c>GET /Transaction/{id}/Flags</c> return: a transaction "carries"
    /// only the flags an analyst selected. The selectable set (for the
    /// flag-picker UI) is a separate concern — see
    /// <see cref="LoadByProductAsync"/> / <c>GET /Flag/Product/{id}/Tab/{tab}</c>.
    ///
    /// <para>Catalogue fields are inner-joined for display. Scope fields
    /// (tabId, sortOrder, legacyFieldName, flagScopeId) are best-effort: a flag
    /// can be scoped to several tabs and <c>TmX_Transaction_Flag</c> only stores
    /// <c>Flag_ID</c>, so we attach the first active scope for this product
    /// (deterministic by tab, then sort order). Two cheap queries + an in-memory
    /// stitch keep the SQL trivially translatable.</para>
    /// </summary>
    public static async Task<List<TransactionFlagDto>> LoadSetForTransactionAsync(
        IApplicationDbContext db,
        int transactionId,
        int productId,
        CancellationToken ct)
    {
        // 1) The persisted, ACTUALLY-FLAGGED rows for this transaction
        //    (IsFlagged = true) + their catalogue entry + evidence file (LEFT
        //    JOIN). Catalogue is NOT filtered on active so a set flag still shows
        //    even if the catalogue entry was later retired. Rows that were
        //    toggled off (IsFlagged = false) are excluded — a transaction
        //    "carries" only the flags that are set.
        var setRows = await (
            from tf in db.TransactionFlags.AsNoTracking()
                 .Where(t => t.TransactionId == transactionId && t.IsFlagged)
            join c in db.FlagCatalogues.AsNoTracking() on tf.FlagId equals c.FlagId
            from doc in db.Documents.AsNoTracking()
                  .Where(d => d.DocumentId == tf.EvidenceDocumentId).DefaultIfEmpty()
            select new
            {
                tf,
                c,
                evidenceFileName = doc == null ? null : doc.OriginalFileName
            }).ToListAsync(ct);

        if (setRows.Count == 0) return new List<TransactionFlagDto>();

        // 2) Best-effort scope context for those flags under this product.
        var flagIds = setRows.Select(r => r.tf.FlagId).Distinct().ToList();
        var scopes = await db.FlagScopes.AsNoTracking()
            .Where(x => flagIds.Contains(x.FlagId)
                     && (x.ProductId == productId || x.ProductId == null)
                     && x.ActiveFlag)
            .OrderBy(x => x.TabId).ThenBy(x => x.SortOrder)
            .ToListAsync(ct);
        var scopeByFlag = scopes
            .GroupBy(x => x.FlagId)
            .ToDictionary(g => g.Key, g => g.First());

        // 2b) Resolve SetBy user-id GUIDs → display names (TmX_User), so the FE
        //     shows the actual user, not a GUID, on both the embedded flag list
        //     and the standalone GET /Transaction/{id}/Flags.
        var setByIds = setRows
            .Select(r => r.tf.SetBy)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var setByNames = setByIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (await db.Users.AsNoTracking()
                .Where(u => setByIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
                .ToListAsync(ct))
                .ToDictionary(
                    u => u.Id,
                    u =>
                    {
                        var full = $"{u.FirstName} {u.LastName}".Trim();
                        return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? u.Id) : full;
                    },
                    StringComparer.OrdinalIgnoreCase);
        string? ResolveSetBy(string? key) =>
            !string.IsNullOrWhiteSpace(key) && setByNames.TryGetValue(key!, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n
                : key;

        return setRows
            .OrderBy(r => r.c.FlagName)
            .Select(r =>
            {
                scopeByFlag.TryGetValue(r.tf.FlagId, out var s);
                return new TransactionFlagDto
                {
                    flagScopeId        = s?.FlagScopeId ?? 0,
                    productId          = s?.ProductId,
                    tabId              = s?.TabId,
                    sortOrder          = s?.SortOrder ?? 0,
                    legacyFieldName    = s?.LegacyFieldName,

                    flagId             = r.c.FlagId,
                    flagCode           = r.c.FlagCode,
                    flagName           = r.c.FlagName,
                    flagDescription    = r.c.FlagDescription,
                    flagTypeLkpId      = r.c.FlagTypeLkpId,
                    severityLkpId      = r.c.SeverityLkpId,
                    defaultWeight      = r.c.DefaultWeight,
                    requiresEvidence   = r.c.RequiresEvidence,

                    transactionFlagId  = r.tf.TransactionFlagId,
                    isFlagged          = r.tf.IsFlagged,
                    evidenceDocumentId = r.tf.EvidenceDocumentId,
                    evidenceFileName   = r.evidenceFileName,
                    analystNotes       = r.tf.AnalystNotes,
                    setBy              = ResolveSetBy(r.tf.SetBy),
                    setDate            = r.tf.SetDate
                };
            })
            .ToList();
    }

    /// <summary>
    /// "Which flags apply to this product (and optionally tab)" — no
    /// transaction context. Used during the create-flow before a
    /// <c>Transaction_Id</c> exists, and for any read-side admin /
    /// catalogue browse.
    ///
    /// Same DTO as <see cref="LoadAsync"/>; the transaction-state
    /// fields come back null / false because there's no
    /// <c>TmX_Transaction_Flag</c> to LEFT JOIN against. FE renders
    /// the flag panel identically — every flag starts unticked.
    /// </summary>
    public static async Task<List<TransactionFlagDto>> LoadByProductAsync(
        IApplicationDbContext db,
        int productId,
        int? tabId,
        CancellationToken ct)
    {
        // The manual flag-picker must show ONLY manual flags — never the
        // rule-driven automated flags (Flag_Type = AUTOMATED, raised by the
        // auto-flag engine). Resolve the AUTOMATED lookup id once and exclude
        // catalogue entries of that type. Untyped/legacy flags (NULL type) are
        // treated as manual and kept.
        var automatedTypeId = await db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "FLAG_TYPE" && l.HiddenValue == "AUTOMATED")
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

        var query =
            from s in db.FlagScopes.AsNoTracking()
            // Product-specific scopes PLUS "all products" (global) scopes.
            where (s.ProductId == productId || s.ProductId == null)
               && s.ActiveFlag

            join c in db.FlagCatalogues.AsNoTracking()
                 .Where(c => c.ActiveFlag && !c.IsDeleted
                          && (automatedTypeId == null
                              || c.FlagTypeLkpId == null
                              || c.FlagTypeLkpId != automatedTypeId)) on s.FlagId equals c.FlagId

            // Tab filter applies only to product-specific scopes; global
            // (all-products) scopes always surface regardless of tab.
            where tabId == null || s.ProductId == null || s.TabId == tabId

            orderby s.TabId, s.SortOrder, c.FlagName

            select new TransactionFlagDto
            {
                flagScopeId         = s.FlagScopeId,
                productId           = s.ProductId,
                tabId               = s.TabId,
                sortOrder           = s.SortOrder,
                legacyFieldName     = s.LegacyFieldName,

                flagId              = c.FlagId,
                flagCode            = c.FlagCode,
                flagName            = c.FlagName,
                flagDescription     = c.FlagDescription,
                flagTypeLkpId       = c.FlagTypeLkpId,
                severityLkpId       = c.SeverityLkpId,
                defaultWeight       = c.DefaultWeight,
                requiresEvidence    = c.RequiresEvidence,

                transactionFlagId   = null,
                isFlagged           = false,
                evidenceDocumentId  = null,
                evidenceFileName    = null,
                analystNotes        = null,
                setBy               = null,
                setDate             = null
            };

        return await query.ToListAsync(ct);
    }
}
