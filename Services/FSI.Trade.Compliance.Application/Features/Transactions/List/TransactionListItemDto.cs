namespace FSI.Trade.Compliance.Application.Features.Transactions.List;

/// <summary>
/// FE-facing shape for one Trade Repository grid row. Mirrors the legacy
/// <c>TransactionVWViewModel</c> columns the FE actually displays.
/// Field names are camelCase to match the rest of the response envelope.
///
/// Note: <see cref="inboxUserId"/> / <see cref="inboxName"/> can be NULL —
/// the <c>TmX_Transaction_VW</c> view collapses inbox rows when more than
/// one actor is assigned, leaving <c>Inbox_User_ID = NULL</c> and
/// <c>Inbox_Name = "Multiple Users"</c>. The FE displays "Multiple Users"
/// or "Unassigned" depending on shape.
/// </summary>
public class TransactionListItemDto
{
    public int       transactionId           { get; set; }
    public string?   transactionNumber       { get; set; }
    public string?   transactionType         { get; set; }
    public DateTime? transactionDate         { get; set; }
    public DateTime  createdDate             { get; set; }

    public int       productId               { get; set; }
    public string    productName             { get; set; } = "";

    public string?   customerCode            { get; set; }
    public string?   customerName            { get; set; }
    public string?   nationalIdentifierValue { get; set; }

    public string?   branchName              { get; set; }
    public string?   creatorName             { get; set; }
    public string?   currentState            { get; set; }

    public string?   inboxUserId             { get; set; }   // NULL when >1 actor
    public string?   inboxName               { get; set; }   // "Multiple Users" when >1 actor
    public string?   status                  { get; set; }

    public Guid?     processInstanceId       { get; set; }
    public int       transactionStatusLkp    { get; set; }

    // ------------------------------------------------------------------
    // Trade Repository redesign (Option A) — enrichment fields.
    // These do NOT live on TmX_Transaction_VW; the handler fills them via
    // a single batched secondary query over the current page's ids
    // (sub-product tab name + flagged-count). Both are optional/zero so a
    // legacy or single-tab (KYC) row simply omits the chip / shows 0.
    // ------------------------------------------------------------------

    /// <summary>
    /// Selected sub-product tab name (TmX_Tab.Tab_Name) for non-KYC products.
    /// NULL for KYC / single-tab products and legacy rows with no Sub_Product_Tab_Id.
    /// </summary>
    public string?   subProductName          { get; set; }

    /// <summary>
    /// Count of currently-raised flags (TmX_Transaction_Flag where Is_Flagged=1).
    /// Drives the row's "risk" cue. 0 when none.
    /// </summary>
    public int       flagCount               { get; set; }

    /// <summary>
    /// Highest severity among the row's currently-raised flags — the display
    /// label from FLAG_SEVERITY (e.g. "Critical"). Derived from
    /// TmX_Transaction_Flag → TmX_Flag_Catalogue.Severity_Lkp_Id; NULL when the
    /// row has no flagged rows or none carry a severity. This is real flag-backed
    /// data (no per-transaction severity column exists).
    /// </summary>
    public string?   severity                { get; set; }

    /// <summary>Numeric rank of <see cref="severity"/> (Critical=4…Info=0) for FE sorting/colour. 0 when none.</summary>
    public int       severityRank            { get; set; }
}
