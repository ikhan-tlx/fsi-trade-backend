using FSI.Trade.Compliance.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Common.Extensions;

/// <summary>
/// Reusable paging primitives used by every grid endpoint:
///
/// <list type="bullet">
///   <item><see cref="ToPagedResultAsync{T}"/> — runs <c>Count + Skip + Take + ToListAsync</c>
///         in one extension call. Caller pre-applies any sort / where.</item>
///   <item><see cref="ToPagedResultAsync{TSource, TItem}"/> — same, but with a projection step.
///         Avoids hauling unneeded columns over the wire when the entity is heavy
///         (e.g. <c>TmX_Transaction_VW</c> with 35+ columns).</item>
///   <item><see cref="ParseSort"/> — turns the <c>"field-dir,field-dir"</c>
///         convention from <see cref="PagedQuery.Sort"/> into typed
///         <c>(Field, Descending)</c> tuples. Each handler still owns the
///         compile-safe field switch so we never reflect into untrusted column
///         names.</item>
/// </list>
///
/// Sort syntax (from <see cref="PagedQuery.Sort"/>): "<field>-<asc|desc>"
/// joined by commas. Examples: "createdDate-desc", "lastName-asc,firstName-asc".
/// The "asc" suffix is optional — anything other than "desc" is treated as asc.
/// </summary>
public static class PagedQueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T>  source,
        PagedQuery          req,
        CancellationToken   ct)
    {
        var total = await source.CountAsync(ct);
        var items = await source
            .Skip(req.Skip)
            .Take(req.Take)
            .ToListAsync(ct);

        return new PagedResult<T>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    public static async Task<PagedResult<TItem>> ToPagedResultAsync<TSource, TItem>(
        this IQueryable<TSource>                source,
        PagedQuery                              req,
        Func<IQueryable<TSource>, IQueryable<TItem>> project,
        CancellationToken                       ct)
    {
        var total = await source.CountAsync(ct);
        var items = await project(source)
            .Skip(req.Skip)
            .Take(req.Take)
            .ToListAsync(ct);

        return new PagedResult<TItem>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    /// <summary>
    /// Parses the <see cref="PagedQuery.Sort"/> string into typed tokens. Each
    /// handler then matches its own field allow-list against the field name
    /// (compile-safe, no reflection). Returns an empty sequence when the input
    /// is null / whitespace — callers should fall back to their own default
    /// ordering in that case.
    /// </summary>
    public static IEnumerable<(string Field, bool Descending)> ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            yield break;

        foreach (var token in sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('-', 2, StringSplitOptions.TrimEntries);
            var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "";
            if (field.Length == 0) continue;
            var dir   = parts.ElementAtOrDefault(1)?.ToLowerInvariant();
            var desc  = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            yield return (field, desc);
        }
    }
}
