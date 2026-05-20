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
}
