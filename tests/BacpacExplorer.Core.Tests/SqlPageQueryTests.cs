using BacpacExplorer.Core.Models;
using BacpacExplorer.Core.Querying;

namespace BacpacExplorer.Core.Tests;

public sealed class SqlPageQueryTests
{
    [Fact]
    public void Build_QuotesSchemaAndTableAndUsesParameterizedBounds()
    {
        var table = new DatabaseTable("odd] schema", "Order; DROP TABLE x--", []);

        var sql = SqlPageQuery.Build(table);

        Assert.Contains("FROM [odd]] schema].[Order; DROP TABLE x--]", sql);
        Assert.Contains("OFFSET @offset ROWS FETCH NEXT @fetch ROWS ONLY", sql);
    }
}
