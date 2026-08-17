namespace BacpacExplorer.Core.Database;

/// <summary>
/// Holds a named operating-system semaphore for the lifetime of a managed temporary database.
/// The named kernel object exists while a process holds it, allowing orphan cleanup to distinguish
/// abandoned databases from databases belonging to another running application instance.
/// </summary>
internal sealed class TemporaryDatabaseLease : IDisposable
{
    internal const string DatabasePrefix = "BacpacExplorer_Managed_";
    private const string LeasePrefix = @"Local\BacpacExplorer.Database.";
    private readonly Semaphore _semaphore;
    private bool _disposed;

    private TemporaryDatabaseLease(string databaseName, Semaphore semaphore)
    {
        DatabaseName = databaseName;
        _semaphore = semaphore;
    }

    public string DatabaseName { get; }

    public static TemporaryDatabaseLease Create()
    {
        while (true)
        {
            var identifier = Guid.NewGuid().ToString("N");
            var databaseName = $"{DatabasePrefix}{identifier}";
            var semaphore = new Semaphore(0, 1, LeasePrefix + identifier, out var createdNew);
            if (createdNew)
                return new TemporaryDatabaseLease(databaseName, semaphore);

            semaphore.Dispose();
        }
    }

    public static bool TryAcquireForCleanup(string databaseName, out TemporaryDatabaseLease? lease)
    {
        lease = null;
        if (!TryGetIdentifier(databaseName, out var identifier))
            return false;

        var semaphore = new Semaphore(0, 1, LeasePrefix + identifier, out var createdNew);
        if (!createdNew)
        {
            semaphore.Dispose();
            return false;
        }

        lease = new TemporaryDatabaseLease(databaseName, semaphore);
        return true;
    }

    internal static bool IsManagedDatabaseName(string databaseName) =>
        TryGetIdentifier(databaseName, out _);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _semaphore.Dispose();
    }

    private static bool TryGetIdentifier(string databaseName, out string identifier)
    {
        identifier = string.Empty;
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            return false;

        identifier = databaseName[DatabasePrefix.Length..];
        return Guid.TryParseExact(identifier, "N", out _);
    }
}
