using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Read;

/// <summary>
/// Shared LEFT-JOIN projection that drives both
/// <c>GET /Transaction/{id}</c> (embedded flag list) and
/// <c>GET /Transaction/{id}/Flags</c> (standalone). One place to evolve
/// the query keeps the two endpoints permanently in sync.
///
/// Semantics:
///   • Every active <c>TmX_Flag_Scope</c> row for the transaction's
///     product becomes a result row.
///   • If the transaction has a matching <c>TmX_Transaction_Flag</c>
///     row, its state is folded in (isFlagged, evidence, notes,
///     attribution).
///   • Otherwise <c>isFlagged = false</c>, attribution fields null.
///   • Catalogue must also be active — retired catalogue entries are
///     filtered out even if a stale scope row points at them.
///
/// Ordering: tab first (form-render groups by tab), then sort order,
/// then flag name as a deterministic tie-breaker.
/// </summary>
public static class TransactionFlagProjection
{
    public static async Task<List<TransactionFlagDto>> LoadAsync(
        IApplicationDbContext db,
        int transactionId,
        int productId,
        CancellationToken ct)
    {
        // The LEFT JOIN expressed as a query-syntax LINQ — DefaultIfEmpty
        // is the EF Core idiom for outer joins. The transaction-flag side
        // is filtered by TransactionId BEFORE the join (rather than in the
        // ON clause) so EF generates a clean LEFT JOIN with a sargable
        // predicate instead of pushing the filter into the outer WHERE.
        var txFlags =
            from f in db.TransactionFlags.AsNoTracking()
            where f.TransactionId == transactionId
            select f;

        var query =
            from s in db.FlagScopes.AsNoTracking()
            where s.ProductId  == productId
               && s.ActiveFlag == true

            join c in db.FlagCatalogues.AsNoTracking()
                 .Where(c => c.ActiveFlag) on s.FlagId equals c.FlagId

            join tf in txFlags on s.FlagId equals tf.FlagId into tfj
            from tf in tfj.DefaultIfEmpty()

            join doc in db.Documents.AsNoTracking()
                   on tf!.EvidenceDocumentId equals doc.DocumentId into docj
            from doc in docj.DefaultIfEmpty()

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
                flagCategoryLkpId   = c.FlagCategoryLkpId,
                severityLkpId       = c.SeverityLkpId,
                defaultWeight       = c.DefaultWeight,
                requiresEvidence    = c.RequiresEvidence,

                transactionFlagId   = tf == null ? (int?)null  : tf.TransactionFlagId,
                isFlagged           = tf != null && tf.IsFlagged,
                evidenceDocumentId  = tf == null ? (int?)null  : tf.EvidenceDocumentId,
                evidenceFileName    = doc == null ? null       : doc.OriginalFileName,
                analystNotes        = tf == null ? null        : tf.AnalystNotes,
                setBy               = tf == null ? null        : tf.SetBy,
                setDate             = tf == null ? (DateTime?)null : tf.SetDate
            };

        return await query.ToListAsync(ct);
    }
}
