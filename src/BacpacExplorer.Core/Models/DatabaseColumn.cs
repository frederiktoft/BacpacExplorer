namespace BacpacExplorer.Core.Models;

/// <summary>
/// Immutable display model for a table column and its reconstructed SQL type and nullability.
/// </summary>
public sealed record DatabaseColumn(
    string Name,
    string SqlType,
    bool IsNullable)
{
    public string Nullability => IsNullable ? "NULL" : "NOT NULL";
}
