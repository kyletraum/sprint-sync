namespace SprintSync.Api.Contracts;

/// <summary>
/// The single offset-pagination envelope used by all list endpoints
/// (Principle III).
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Offset paging parameters with sane bounds.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 50;

    public static PageRequest From(int? page, int? pageSize)
    {
        var p = page is > 0 ? page.Value : 1;
        var size = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;
        return new PageRequest(p, size);
    }

    /// <summary>
    /// Rows to skip. Computed in <c>long</c> and clamped so a very large page
    /// number cannot overflow <c>int</c> into a negative SQL OFFSET (a 500) — an
    /// over-large page coerces to an empty final page, matching the documented
    /// "coerced, not rejected" paging contract (P1-3).
    /// </summary>
    public int Skip
    {
        get
        {
            var skip = (long)(Page - 1) * PageSize;
            return skip > int.MaxValue ? int.MaxValue : (int)skip;
        }
    }
}
