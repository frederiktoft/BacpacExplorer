using BacpacExplorer.Core.Querying;

namespace BacpacExplorer.Core.Tests;

public sealed class QueryResultColumnNameTests
{
    [Theory]
    [InlineData(new[] { "A", "A", "A" }, new[] { "A", "A_2", "A_3" })]
    [InlineData(new[] { "A", "A_2", "A" }, new[] { "A", "A_2", "A_3" })]
    [InlineData(new[] { "A", "a" }, new[] { "A", "a_2" })]
    public void MakeUniqueColumnNames_AvoidsAllGeneratedNameCollisions(
        string[] rawNames,
        string[] expectedNames)
    {
        Assert.Equal(expectedNames, ReadOnlySqlQueryService.MakeUniqueColumnNames(rawNames));
    }

    [Fact]
    public void MakeUniqueColumnNames_AvoidsCollisionWithGeneratedUnnamedColumn()
    {
        var generatedName = ReadOnlySqlQueryService.MakeUniqueColumnNames([string.Empty])[0];

        var names = ReadOnlySqlQueryService.MakeUniqueColumnNames([string.Empty, generatedName]);

        Assert.Equal(generatedName, names[0]);
        Assert.Equal($"{generatedName}_2", names[1]);
    }
}
