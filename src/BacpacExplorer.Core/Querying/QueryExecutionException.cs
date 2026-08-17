namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Represents query validation or execution failure while retaining the underlying parser or SQL exception for logging.
/// </summary>
public sealed class QueryExecutionException : Exception
{
    public QueryExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
