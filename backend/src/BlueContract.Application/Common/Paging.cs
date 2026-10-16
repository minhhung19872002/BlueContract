using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Common;

/// <summary>Server-side paging/sorting/search parameters shared by every list endpoint (spec §9.3).</summary>
public record PageQuery
{
    public const int MaxPageSize = 200;

    /// <summary>Row cap for Excel exports.</summary>
    public const int MaxExportRows = 10_000;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? Search { get; init; }

    /// <summary>Property name to sort by (whitelisted per endpoint).</summary>
    public string? SortBy { get; init; }

    public bool SortDesc { get; init; }

    /// <summary>Set server-side by export endpoints to lift the page-size cap (up to <see cref="MaxExportRows"/>).</summary>
    public bool ForExport { get; init; }

    public int SafePage => Page < 1 ? 1 : Page;

    public int SafePageSize => PageSize switch
    {
        < 1 => 20,
        > MaxExportRows => MaxExportRows,
        > MaxPageSize when !ForExport => MaxPageSize,
        _ => PageSize,
    };
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .ToListAsync(ct);
        return new PagedResult<T>(items, total, page.SafePage, page.SafePageSize);
    }

    /// <summary>Applies sorting from a whitelist map; unknown keys fall back to <paramref name="defaultSort"/>.</summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        PageQuery page,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sorts,
        string defaultSort,
        bool defaultDesc = false)
    {
        var useDefault = string.IsNullOrWhiteSpace(page.SortBy) || !sorts.ContainsKey(page.SortBy);
        var key = useDefault ? defaultSort : page.SortBy!;
        var desc = useDefault ? defaultDesc : page.SortDesc;
        var selector = sorts[key];
        var ordered = desc ? query.OrderByDescending(selector) : query.OrderBy(selector);
        return ThenById(ordered);
    }

    /// <summary>
    /// Ends every ordering on the unique <c>Id</c> so rows with equal sort values keep one order across pages: walking
    /// page by page never repeats or skips a row (spec §9.3 server paging).
    /// </summary>
    public static IOrderedQueryable<T> ThenById<T>(IOrderedQueryable<T> query)
    {
        var id = typeof(T).GetProperty("Id");
        if (id is null)
        {
            return query;
        }

        var row = Expression.Parameter(typeof(T), "x");
        var key = Expression.Lambda(Expression.Property(row, id), row);
        var call = Expression.Call(typeof(Queryable), nameof(Queryable.ThenBy), [typeof(T), id.PropertyType], query.Expression, Expression.Quote(key));
        return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    /// <summary>Case-insensitive "contains" pattern for ILIKE with escaped wildcards.</summary>
    public static string LikePattern(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
