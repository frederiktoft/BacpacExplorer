using BacpacExplorer.Core.Sql;
using BacpacExplorer.Core.Localization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Dac;

namespace BacpacExplorer.Core.Database;

/// <summary>
/// Imports a BACPAC into a uniquely named SQL Server LocalDB database and removes it when disposed.
/// It creates a contained, SELECT-only user for arbitrary queries and performs conservative cleanup of
/// managed databases left behind by crashed application instances.
/// </summary>
public sealed class LocalDbTemporaryDatabase : ITemporaryDatabase
{
    private const string LocalDbServer = @"(LocalDB)\MSSQLLocalDB";
    private static readonly string ConfigurationStateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BacpacExplorer",
        "LocalDb");
    private static readonly string ConfigurationLockPath = Path.Combine(ConfigurationStateDirectory, "contained-authentication.lock");
    private static readonly string ConfigurationRestoreMarkerPath = Path.Combine(ConfigurationStateDirectory, "restore-contained-authentication.marker");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<LocalDbTemporaryDatabase>? _logger;
    private string? _databaseName;
    private string? _readOnlyUserName;
    private TemporaryDatabaseLease? _databaseLease;
    private bool _disposed;

    public LocalDbTemporaryDatabase(ILogger<LocalDbTemporaryDatabase>? logger = null)
    {
        _logger = logger;
    }

    public bool IsReady { get; private set; }

    public string ConnectionString => IsReady && _databaseName is not null
        ? BuildConnectionString(_databaseName)
        : throw new InvalidOperationException(CoreText.Get("TemporaryDatabaseNotPrepared"));

    public string ReadOnlyUserName => IsReady && _readOnlyUserName is not null
        ? _readOnlyUserName
        : throw new InvalidOperationException(CoreText.Get("TemporaryDatabaseNotPrepared"));

    public async Task PrepareAsync(
        string bacpacPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bacpacPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsReady)
                return;

            progress?.Report(CoreText.Get("StartingLocalDb"));
            await EnsureLocalDbAvailableAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CleanupOrphanedDatabasesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SqlException
                                               or IOException
                                               or UnauthorizedAccessException
                                               or WaitHandleCannotBeOpenedException)
            {
                _logger?.LogWarning(
                    exception,
                    "Could not inspect or remove orphaned temporary LocalDB databases; preparation will continue");
            }

            await using (await AcquireContainedDatabaseAuthenticationScopeAsync(cancellationToken).ConfigureAwait(false))
            {
                _databaseLease = TemporaryDatabaseLease.Create();
                _databaseName = _databaseLease.DatabaseName;
                var databaseName = _databaseName;
                try
                {
                    progress?.Report(CoreText.Get("ImportingData"));
                    await Task.Run(() => Import(bacpacPath, databaseName, progress, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                    _readOnlyUserName = $"BacpacExplorerReader_{Guid.NewGuid():N}";
                    await CreateReadOnlyUserAsync(databaseName, _readOnlyUserName, cancellationToken).ConfigureAwait(false);
                    IsReady = true;
                    progress?.Report(CoreText.Get("ImportComplete"));
                }
                catch (OperationCanceledException)
                {
                    await DropDatabaseAsync(databaseName, CancellationToken.None).ConfigureAwait(false);
                    ReleaseDatabaseLease();
                    _databaseName = null;
                    _readOnlyUserName = null;
                    throw;
                }
                catch (Exception exception) when (exception is DacServicesException or IOException or UnauthorizedAccessException or SqlException)
                {
                    await DropDatabaseAsync(databaseName, CancellationToken.None).ConfigureAwait(false);
                    ReleaseDatabaseLease();
                    _databaseName = null;
                    _readOnlyUserName = null;
                    throw new TemporaryDatabaseException(
                        CoreText.Format("ImportFailed", exception.Message),
                        exception);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_databaseName is not null)
                await DropDatabaseAsync(_databaseName, CancellationToken.None).ConfigureAwait(false);

            ReleaseDatabaseLease();
            _databaseName = null;
            _readOnlyUserName = null;
            IsReady = false;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static void Import(
        string bacpacPath,
        string databaseName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var package = BacPackage.Load(bacpacPath, DacSchemaModelStorageType.Memory);
        var services = new DacServices(BuildConnectionString("master"));
        services.ProgressChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Message))
                progress?.Report(args.Message);
        };
        services.ImportBacpac(package, databaseName, cancellationToken);
    }

    private static async Task EnsureLocalDbAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(BuildConnectionString("master"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException exception)
        {
            throw new TemporaryDatabaseException(
                CoreText.Get("LocalDbUnavailable"),
                exception);
        }
    }

    private async Task<bool> DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        try
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(BuildConnectionString("master"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            var quotedName = SqlIdentifier.Quote(databaseName);
            command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL BEGIN ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quotedName}; END";
            command.Parameters.AddWithValue("@databaseName", databaseName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqlException exception)
        {
            // Cleanup is best-effort. A later orphan-cleanup facility can retry databases
            // whose LocalDB instance is unavailable during application shutdown.
            _logger?.LogWarning(
                exception,
                "Failed to drop temporary LocalDB database {DatabaseName}; orphan cleanup may be required",
                databaseName);
            return false;
        }
    }

    private async Task CleanupOrphanedDatabasesAsync(CancellationToken cancellationToken)
    {
        var databaseNames = new List<string>();
        await using (var connection = new SqlConnection(BuildConnectionString("master")))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE N'BacpacExplorer[_]Managed[_]%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                databaseNames.Add(reader.GetString(0));
        }

        var removedCount = 0;
        foreach (var databaseName in databaseNames)
        {
            if (!TemporaryDatabaseLease.TryAcquireForCleanup(databaseName, out var cleanupLease))
                continue;

            using (cleanupLease)
            {
                if (await DropDatabaseAsync(databaseName, cancellationToken).ConfigureAwait(false))
                    removedCount++;
            }
        }

        if (removedCount > 0)
            _logger?.LogInformation("Removed {DatabaseCount} orphaned temporary LocalDB database(s)", removedCount);
    }

    private void ReleaseDatabaseLease()
    {
        _databaseLease?.Dispose();
        _databaseLease = null;
    }

    private async Task<ContainedDatabaseAuthenticationScope> AcquireContainedDatabaseAuthenticationScopeAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ConfigurationStateDirectory);
        var lockStream = await AcquireConfigurationLockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var restoreWhenFinished = File.Exists(ConfigurationRestoreMarkerPath);
            if (!await ContainedDatabaseAuthenticationIsEnabledAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!restoreWhenFinished)
                {
                    await File.WriteAllTextAsync(
                        ConfigurationRestoreMarkerPath,
                        "BACPAC Explorer enabled this setting and must restore it.",
                        cancellationToken).ConfigureAwait(false);
                    restoreWhenFinished = true;
                }

                await SetContainedDatabaseAuthenticationAsync(enabled: true, cancellationToken).ConfigureAwait(false);
            }

            return new ContainedDatabaseAuthenticationScope(lockStream, restoreWhenFinished, _logger);
        }
        catch
        {
            await lockStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<FileStream> AcquireConfigurationLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    ConfigurationLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> ContainedDatabaseAuthenticationIsEnabledAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = N'contained database authentication';";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task CreateReadOnlyUserAsync(
        string databaseName,
        string userName,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(BuildConnectionString(databaseName));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var quotedUser = SqlIdentifier.Quote(userName);
        command.CommandText = $"CREATE USER {quotedUser} WITHOUT LOGIN; GRANT CONNECT TO {quotedUser}; GRANT SELECT TO {quotedUser};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetContainedDatabaseAuthenticationAsync(bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = enabled
            ? "EXEC sp_configure N'contained database authentication', 1; RECONFIGURE;"
            : "EXEC sp_configure N'contained database authentication', 0; RECONFIGURE WITH OVERRIDE;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildConnectionString(string initialCatalog) =>
        new SqlConnectionStringBuilder
        {
            DataSource = LocalDbServer,
            InitialCatalog = initialCatalog,
            IntegratedSecurity = true,
            Encrypt = false,
            ConnectTimeout = 15,
            ApplicationName = "BacpacExplorer"
        }.ConnectionString;

    /// <summary>
    /// Holds the cross-process configuration lock while an import depends on contained authentication.
    /// A persistent marker records that the application changed the setting, allowing a later run to
    /// restore it even when the process that enabled it terminated unexpectedly.
    /// </summary>
    private sealed class ContainedDatabaseAuthenticationScope(
        FileStream lockStream,
        bool restoreWhenFinished,
        ILogger<LocalDbTemporaryDatabase>? logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!restoreWhenFinished)
                    return;

                try
                {
                    await SetContainedDatabaseAuthenticationAsync(enabled: false, CancellationToken.None).ConfigureAwait(false);
                    File.Delete(ConfigurationRestoreMarkerPath);
                }
                catch (Exception exception) when (exception is SqlException
                                                   or IOException
                                                   or UnauthorizedAccessException
                                                   or InvalidOperationException)
                {
                    logger?.LogWarning(
                        exception,
                        "Could not restore LocalDB contained database authentication; a later run will retry");
                }
            }
            finally
            {
                await lockStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
