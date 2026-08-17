using BacpacExplorer.Core.Sql;

namespace BacpacExplorer.Core.Tests;

public sealed class SqlIdentifierTests
{
    [Fact]
    public void QuoteMultipart_QuotesEveryPartAndEscapesClosingBrackets()
    {
        var result = SqlIdentifier.QuoteMultipart("Some Schema", "Order]History");

        Assert.Equal("[Some Schema].[Order]]History]", result);
    }

    [Theory]
    [InlineData("dbo.Customer", "dbo", "Customer")]
    [InlineData("[Some Schema].[Some Table]", "Some Schema", "Some Table")]
    [InlineData("[odd]]schema].[Order]", "odd]schema", "Order")]
    public void ParseMultipart_ParsesRegularAndQuotedNames(string value, string schema, string table)
    {
        var result = SqlIdentifier.ParseMultipart(value);

        Assert.Equal([schema, table], result);
    }

    [Theory]
    [InlineData("dbo..Customer")]
    [InlineData("[dbo].[Customer")]
    public void ParseMultipart_RejectsMalformedNames(string value)
    {
        Assert.Throws<FormatException>(() => SqlIdentifier.ParseMultipart(value));
    }
}
