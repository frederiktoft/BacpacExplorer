using BacpacExplorer.Core.Models;

namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Builds the bounded SQL used by the table browser. Table names come from trusted DACFx metadata and
/// are already quoted; offsets and page sizes remain parameters rather than being embedded in SQL.
/// </summary>
internal static class SqlPageQuery
{
    public static string Build(DatabaseTable table) => $"""
        SELECT *
        FROM {table.FullName}
        ORDER BY (SELECT NULL)
        OFFSET @offset ROWS FETCH NEXT @fetch ROWS ONLY;
        """;
}
