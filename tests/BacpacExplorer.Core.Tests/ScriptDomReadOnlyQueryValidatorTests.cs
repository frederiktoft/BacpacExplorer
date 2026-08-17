using BacpacExplorer.Core.Querying;

namespace BacpacExplorer.Core.Tests;

public sealed class ScriptDomReadOnlyQueryValidatorTests
{
    private readonly ScriptDomReadOnlyQueryValidator _validator = new();

    [Theory]
    [InlineData("SELECT * FROM [dbo].[Customer];")]
    [InlineData("WITH recent AS (SELECT TOP (10) * FROM dbo.Customer) SELECT * FROM recent;")]
    [InlineData("SELECT COUNT(*) AS [Count] FROM dbo.Customer WHERE Name LIKE N'%test%';")]
    public void Validate_AllowsSingleReadOnlySelect(string sql)
    {
        Assert.True(_validator.Validate(sql).IsValid);
    }

    [Theory]
    [InlineData("DELETE FROM dbo.Customer;")]
    [InlineData("UPDATE dbo.Customer SET Name = N'x';")]
    [InlineData("INSERT dbo.Customer DEFAULT VALUES;")]
    [InlineData("DROP TABLE dbo.Customer;")]
    [InlineData("EXEC dbo.DoSomething;")]
    [InlineData("SELECT 1; SELECT 2;")]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.Customer;")]
    [InlineData("SELECT * FROM master.sys.databases;")]
    [InlineData("SELECT * FROM ServerName.DatabaseName.dbo.Customer;")]
    public void Validate_RejectsStatementsThatAreNotOneReadOnlySelect(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_ReportsParserLocation()
    {
        var result = _validator.Validate("SELECT\nFROM;");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Line", StringComparison.Ordinal));
    }
}
