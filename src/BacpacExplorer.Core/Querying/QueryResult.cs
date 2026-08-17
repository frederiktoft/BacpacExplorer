namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Contains the display-ready rows and messages returned by a read-only query, including whether its row limit was reached.
/// </summary>
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool WasTruncated,
    TimeSpan Elapsed,
    IReadOnlyList<string> Messages);
