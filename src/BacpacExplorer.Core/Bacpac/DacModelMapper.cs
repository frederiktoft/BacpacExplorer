using BacpacExplorer.Core.Models;
using BacpacExplorer.Core.Localization;
using Microsoft.SqlServer.Dac.Model;

namespace BacpacExplorer.Core.Bacpac;

/// <summary>
/// Translates the DACFx object graph into the small immutable schema model used by the rest of the application.
/// Keeping this translation isolated prevents UI and querying code from depending directly on DACFx types.
/// </summary>
internal static class DacModelMapper
{
    public static IReadOnlyList<DatabaseSchema> ReadSchemas(TSqlModel model, CancellationToken cancellationToken)
    {
        var tables = model.GetObjects(DacQueryScopes.UserDefined, ModelSchema.Table)
            .Select(table => ReadTable(table, cancellationToken))
            .OrderBy(table => table.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tables
            .GroupBy(table => table.Schema, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DatabaseSchema(group.Key, group.ToArray()))
            .ToArray();
    }

    private static DatabaseTable ReadTable(TSqlObject table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nameParts = table.Name.Parts;
        if (nameParts.Count < 2)
            throw new DacModelException(CoreText.Format("TableMissingSchema", table.Name));

        var columns = table.GetReferenced(Table.Columns)
            .Select(column => ReadColumn(column, cancellationToken))
            .ToArray();

        return new DatabaseTable(nameParts[^2], nameParts[^1], columns);
    }

    private static DatabaseColumn ReadColumn(TSqlObject column, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = column.Name.Parts.LastOrDefault()
            ?? throw new DacModelException(CoreText.Get("ColumnMissingName"));
        var dataType = column.GetReferenced(Column.DataType).SingleOrDefault()
            ?? throw new DacModelException(CoreText.Format("ColumnMissingType", column.Name));
        var typeName = dataType.Name.Parts.LastOrDefault()
            ?? throw new DacModelException(CoreText.Format("ColumnUnnamedType", column.Name));

        return new DatabaseColumn(
            name,
            SqlTypeFormatter.Format(column, typeName),
            column.GetProperty<bool>(Column.Nullable));
    }
}
