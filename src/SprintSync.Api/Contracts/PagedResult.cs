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

    public int Skip => (Page - 1) * PageSize;
}
