using BacpacExplorer.Core.Database;

namespace BacpacExplorer.Core.Tests;

public sealed class TemporaryDatabaseLeaseTests
{
    [Fact]
    public void ManagedDatabaseName_RequiresTheExactPrefixAndGuidFormat()
    {
        Assert.True(TemporaryDatabaseLease.IsManagedDatabaseName(
            "BacpacExplorer_Managed_0123456789abcdef0123456789abcdef"));
        Assert.False(TemporaryDatabaseLease.IsManagedDatabaseName(
            "BacpacExplorer_0123456789abcdef0123456789abcdef"));
        Assert.False(TemporaryDatabaseLease.IsManagedDatabaseName(
            "BacpacExplorer_Managed_not-a-guid"));
    }

    [Fact]
    public void ActiveLease_CannotBeAcquiredForCleanup()
    {
        using var activeLease = TemporaryDatabaseLease.Create();

        var acquired = TemporaryDatabaseLease.TryAcquireForCleanup(activeLease.DatabaseName, out var cleanupLease);

        Assert.False(acquired);
        Assert.Null(cleanupLease);
    }

    [Fact]
    public void ReleasedLease_CanBeAcquiredForCleanup()
    {
        var activeLease = TemporaryDatabaseLease.Create();
        var databaseName = activeLease.DatabaseName;
        activeLease.Dispose();

        var acquired = TemporaryDatabaseLease.TryAcquireForCleanup(databaseName, out var cleanupLease);

        Assert.True(acquired);
        cleanupLease!.Dispose();
    }
}
