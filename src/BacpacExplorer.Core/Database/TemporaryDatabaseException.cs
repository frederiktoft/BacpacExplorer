namespace BacpacExplorer.Core.Database;

/// <summary>
/// Represents a failure to start LocalDB, import a BACPAC, or prepare its restricted read-only access.
/// </summary>
public sealed class TemporaryDatabaseException : Exception
{
    public TemporaryDatabaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
