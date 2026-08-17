namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Defines execution of a bounded, validated query against the prepared temporary database.
/// </summary>
public interface ISqlQueryService
{
    Task<QueryResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}
