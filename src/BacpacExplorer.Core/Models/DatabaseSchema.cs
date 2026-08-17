namespace BacpacExplorer.Core.Models;

/// <summary>
/// Groups the user tables discovered under one database schema.
/// </summary>
public sealed record DatabaseSchema(
    string Name,
    IReadOnlyList<DatabaseTable> Tables);
