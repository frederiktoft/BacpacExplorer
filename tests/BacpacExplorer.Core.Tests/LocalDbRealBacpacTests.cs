using BacpacExplorer.Core.Bacpac;
using BacpacExplorer.Core.Database;
using BacpacExplorer.Core.Querying;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace BacpacExplorer.Core.Tests;

public sealed class LocalDbRealBacpacTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "LocalDbIntegration")]
    public async Task ImportBrowseAndCleanup_RealBacpac()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("BACPACEXPLORER_RUN_LOCALDB_TESTS"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Set BACPACEXPLORER_RUN_LOCALDB_TESTS=1 to run the LocalDB import test.");
            return;
        }

        var filePath = Directory.GetFiles(Path.Combine(FindSolutionRoot(), "data"), "*.bacpac").First();
        var model = await new BacpacReader().ReadAsync(filePath);
        var existingUnmanagedExplorerDatabases = (await GetExplorerDatabaseNamesAsync())
            .Where(name => !TemporaryDatabaseLease.IsManagedDatabaseName(name))
            .ToArray();
        var orphanedDatabaseName = $"{TemporaryDatabaseLease.DatabasePrefix}{Guid.NewGuid():N}";
        await CreateDatabaseAsync(orphanedDatabaseName);
        string databaseName;
        var foundData = false;

        try
        {
            await using (var temporaryDatabase = new LocalDbTemporaryDatabase())
            {
                await temporaryDatabase.PrepareAsync(filePath);
                Assert.False(await DatabaseExistsAsync(orphanedDatabaseName), "The orphaned temporary database was not removed.");
                databaseName = new SqlConnectionStringBuilder(temporaryDatabase.ConnectionString).InitialCatalog;
                var browser = new SqlDataBrowser(temporaryDatabase.ConnectionString);
                var queryService = new ReadOnlySqlQueryService(
                    temporaryDatabase.ConnectionString,
                    temporaryDatabase.ReadOnlyUserName);

                var propertyDataTable = model.Schemas
                    .SelectMany(schema => schema.Tables)
                    .Single(table => table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase)
                        && table.Name.Equals("umbracoPropertyData", StringComparison.OrdinalIgnoreCase));
                var propertyDataPage = await browser.ReadPageAsync(propertyDataTable, 0, 5);
                Assert.NotEmpty(propertyDataPage.Rows);
                output.WriteLine("dbo.umbracoPropertyData returned {0} row(s) in a 5-row bounded page.", propertyDataPage.Rows.Count);

                var queryResult = await queryService.ExecuteAsync(
                    "SELECT TOP (5) * FROM [dbo].[umbracoPropertyData];");
                Assert.Equal(5, queryResult.Rows.Count);
                Assert.False(queryResult.WasTruncated);
                var boundedResult = await queryService.ExecuteAsync(
                    "SELECT TOP (1100) a.[object_id] FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b;");
                Assert.Equal(1_000, boundedResult.Rows.Count);
                Assert.True(boundedResult.WasTruncated);
                var collidingColumnResult = await queryService.ExecuteAsync(
                    "SELECT 1 AS [A], 2 AS [A_2], 3 AS [A];");
                Assert.Equal(["A", "A_2", "A_3"], collidingColumnResult.Columns);
                await Assert.ThrowsAsync<QueryExecutionException>(() =>
                    queryService.ExecuteAsync("DELETE FROM [dbo].[umbracoPropertyData];"));
                await Assert.ThrowsAsync<QueryExecutionException>(() =>
                    queryService.ExecuteAsync("SELECT name FROM [master].[sys].[databases];"));
                await Assert.ThrowsAsync<SqlException>(() => AssertRestrictedUserCannotDeleteAsync(
                    temporaryDatabase.ConnectionString,
                    temporaryDatabase.ReadOnlyUserName));

                foreach (var table in model.Schemas.SelectMany(schema => schema.Tables))
                {
                    var page = await browser.ReadPageAsync(table, 0, 5);
                    if (page.Rows.Count == 0)
                        continue;

                    Assert.Equal(table.Columns.Count, page.Columns.Count);
                    Assert.All(page.Rows, row => Assert.Equal(page.Columns.Count, row.Count));
                    foundData = true;
                    break;
                }

                Assert.True(foundData, "The imported BACPAC did not contain a readable row in any user table.");
            }

            Assert.False(await DatabaseExistsAsync(databaseName), "The temporary database was not removed after disposal.");
            var remainingUnmanagedExplorerDatabases = (await GetExplorerDatabaseNamesAsync())
                .Where(name => !TemporaryDatabaseLease.IsManagedDatabaseName(name))
                .ToArray();
            Assert.Equal(existingUnmanagedExplorerDatabases, remainingUnmanagedExplorerDatabases);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(orphanedDatabaseName);
        }
    }

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseIfExistsAsync(string databaseName)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL DROP DATABASE [{databaseName}];";
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name;";
        command.Parameters.AddWithValue("@name", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task AssertRestrictedUserCannotDeleteAsync(string connectionString, string userName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var escapedUser = userName.Replace("'", "''", StringComparison.Ordinal);
        command.CommandText = $"EXECUTE AS USER = N'{escapedUser}'; DELETE TOP (0) FROM [dbo].[umbracoPropertyData];";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> GetExplorerDatabaseNamesAsync()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE N'BacpacExplorer[_]%' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names.ToArray();
    }

    private static string MasterConnectionString => new SqlConnectionStringBuilder
    {
        DataSource = @"(LocalDB)\MSSQLLocalDB",
        InitialCatalog = "master",
        IntegratedSecurity = true,
        Encrypt = false
    }.ConnectionString;

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BacpacExplorer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the solution root from the test output directory.");
    }
}
