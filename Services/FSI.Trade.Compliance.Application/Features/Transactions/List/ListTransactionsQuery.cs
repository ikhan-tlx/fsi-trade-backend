using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.List;

/// <summary>
/// Trade Repository grid feed. Mirrors the legacy
/// <c>TransactionService.GetList(request, userId)</c> behaviour:
///
/// <list type="bullet">
///   <item>If the caller has a <c>Location_ID</c>, scope to that location +
///         all descendants (via <c>ILocationHierarchyService</c>).</item>
///   <item>Otherwise, scope to every <c>Company_Branch_Id</c> in the
///         caller's branch-user mapping (effective today).</item>
///   <item>OR (always) anything they themselves created
///         (<c>Creator_Id == userId</c>).</item>
/// </list>
///
/// Inherits <see cref="PagedQuery"/> for <c>Page / PageSize / Sort / Filter</c>.
/// </summary>
public class ListTransactionsQuery : PagedQuery, IRequest<PagedResult<TransactionListItemDto>>
{
    // ------------------------------------------------------------------
    // Trade Repository redesign (Option A) — optional structured filters
    // powering the KPI cards + product segment. All additive and combine
    // with AND on top of the scope predicate and freetext Filter, so the
    // grid pages correctly across the WHOLE narrowed set (not just the
    // current page). Omit them and the list behaves exactly as before.
    // ------------------------------------------------------------------

    /// <summary>Only rows currently in the caller's own inbox.</summary>
    public bool? Mine         { get; set; }

    /// <summary>Only rows with at least one currently-raised flag.</summary>
    public bool? Flagged      { get; set; }

    /// <summary>Only rows created today (server date).</summary>
    public bool? CreatedToday { get; set; }

    /// <summary>Restrict to one product (segment control).</summary>
    public int?  ProductId    { get; set; }

    /// <summary>Substring match on the workflow status string.</summary>
    public string? Status     { get; set; }
}
