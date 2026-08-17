namespace BacpacExplorer.Core.Models;

/// <summary>
/// Immutable top-level description of an opened BACPAC, including its source file, schemas, and archive entries.
/// </summary>
public sealed record BacpacDatabase(
    string FilePath,
    string DatabaseName,
    long FileSize,
    IReadOnlyList<DatabaseSchema> Schemas,
    IReadOnlyList<string> PackageEntries)
{
    public string FileName => Path.GetFileName(FilePath);
    public int TableCount => Schemas.Sum(schema => schema.Tables.Count);
}
