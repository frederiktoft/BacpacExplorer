using BacpacExplorer.Core.Bacpac;
using BacpacExplorer.Core.Models;
using Microsoft.SqlServer.Dac.Model;

namespace BacpacExplorer.Core.Tests;

public sealed class DacModelMapperTests
{
    [Fact]
    public void ReadSchemas_DiscoversAwkwardNamesAndCompleteColumnMetadataInSourceOrder()
    {
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        model.AddObjects("CREATE SCHEMA [Some Schema];");
        model.AddObjects("""
            CREATE TABLE [Some Schema].[Order]] History]
            (
                [Id] bigint NOT NULL,
                [Name] nvarchar(200) NOT NULL,
                [Notes] nvarchar(max) NULL,
                [Amount] decimal(18,2) NULL,
                [CreatedAt] datetime2(7) NOT NULL,
                [Payload] varbinary(max) NULL
            );
            """);

        var schemas = DacModelMapper.ReadSchemas(model, CancellationToken.None);

        var schema = Assert.Single(schemas);
        Assert.Equal("Some Schema", schema.Name);
        var table = Assert.Single(schema.Tables);
        Assert.Equal("Order] History", table.Name);
        Assert.Equal("[Some Schema].[Order]] History]", table.FullName);
        Assert.Collection(table.Columns,
            column => AssertColumn(column, "Id", "bigint", false),
            column => AssertColumn(column, "Name", "nvarchar(200)", false),
            column => AssertColumn(column, "Notes", "nvarchar(max)", true),
            column => AssertColumn(column, "Amount", "decimal(18,2)", true),
            column => AssertColumn(column, "CreatedAt", "datetime2(7)", false),
            column => AssertColumn(column, "Payload", "varbinary(max)", true));
    }

    private static void AssertColumn(DatabaseColumn column, string name, string sqlType, bool nullable)
    {
        Assert.Equal(name, column.Name);
        Assert.Equal(sqlType, column.SqlType);
        Assert.Equal(nullable, column.IsNullable);
    }
}
