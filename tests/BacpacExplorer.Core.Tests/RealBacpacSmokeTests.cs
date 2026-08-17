using BacpacExplorer.Core.Bacpac;
using Xunit.Abstractions;

namespace BacpacExplorer.Core.Tests;

public sealed class RealBacpacSmokeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task DataDirectoryBacpacs_LoadAndContainDiscoverableSchema()
    {
        var solutionRoot = FindSolutionRoot();
        var dataDirectory = Path.Combine(solutionRoot, "data");
        if (!Directory.Exists(dataDirectory))
        {
            output.WriteLine("No data directory is present; optional real-BACPAC smoke test skipped.");
            return;
        }

        var filePaths = Directory.GetFiles(dataDirectory, "*.bacpac", SearchOption.TopDirectoryOnly);
        if (filePaths.Length == 0)
        {
            output.WriteLine("No BACPAC fixtures are present; optional real-BACPAC smoke test skipped.");
            return;
        }

        var reader = new BacpacReader();
        foreach (var filePath in filePaths)
        {
            var database = await reader.ReadAsync(filePath, CancellationToken.None);

            Assert.NotEmpty(database.Schemas);
            Assert.True(database.TableCount > 0, $"{database.FileName} contains no discoverable user tables.");
            Assert.Contains(database.Schemas.SelectMany(schema => schema.Tables), table => table.Columns.Count > 0);
            var columnCount = database.Schemas.SelectMany(schema => schema.Tables).Sum(table => table.Columns.Count);
            Assert.All(database.Schemas.SelectMany(schema => schema.Tables).SelectMany(table => table.Columns),
                column => Assert.False(string.IsNullOrWhiteSpace(column.SqlType)));
            output.WriteLine("{0}: {1} schemas, {2} tables, {3} columns",
                database.FileName, database.Schemas.Count, database.TableCount, columnCount);
        }
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BacpacExplorer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the solution root from the test output directory.");
    }
}
