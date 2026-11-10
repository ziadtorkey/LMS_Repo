namespace Lms.Shared;

/// <summary>One page of a server-paged list.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Paging limits (M1 Global Constraints): <c>page</c> at least 1, <c>pageSize</c> default 50, max 200.</summary>
public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}
