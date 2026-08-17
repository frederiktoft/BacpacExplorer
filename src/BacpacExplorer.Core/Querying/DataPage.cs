namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Contains one bounded table page plus the navigation state needed by the Explorer UI.
/// </summary>
public sealed record DataPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int Offset,
    int PageSize,
    bool HasNextPage)
{
    public bool HasPreviousPage => Offset > 0;
}
