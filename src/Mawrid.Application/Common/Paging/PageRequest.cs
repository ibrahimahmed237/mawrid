namespace Mawrid.Application.Common.Paging;

/// <summary>Page number (from 1) and page size (1 to 100) for list queries.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>One page of results. The same shape is used by every list endpoint.</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
