namespace BacpacExplorer.Core.Database;

/// <summary>
/// Represents the disposable database created from a BACPAC when row browsing or SQL querying is requested.
/// Implementations expose only the connection details needed by read-only consumers and own cleanup of the import.
/// </summary>
public interface ITemporaryDatabase : IAsyncDisposable
{
    bool IsReady { get; }
    string ConnectionString { get; }
    string ReadOnlyUserName { get; }

    Task PrepareAsync(
        string bacpacPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
