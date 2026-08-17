using BacpacExplorer.Core.Sql;

namespace BacpacExplorer.Core.Models;

/// <summary>
/// Immutable table metadata used by both the schema tree and generated paging queries.
/// </summary>
public sealed record DatabaseTable(
    string Schema,
    string Name,
    IReadOnlyList<DatabaseColumn> Columns)
{
    public string FullName => SqlIdentifier.QuoteMultipart(Schema, Name);
}
