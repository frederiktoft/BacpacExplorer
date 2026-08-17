using Microsoft.SqlServer.Dac.Model;

namespace BacpacExplorer.Core.Bacpac;

/// <summary>
/// Reconstructs a familiar SQL type declaration from the separate type properties exposed by DACFx.
/// This keeps details such as <c>nvarchar(max)</c>, decimal precision/scale, and qualified
/// user-defined type names out of the model-mapping code.
/// </summary>
internal static class SqlTypeFormatter
{
    private static readonly HashSet<string> LengthTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "char", "nchar", "nvarchar", "varbinary", "varchar"
    };

    private static readonly HashSet<string> ScaleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "datetime2", "datetimeoffset", "time"
    };

    public static string Format(TSqlObject column, string typeName)
    {
        var qualifiedType = column.GetReferenced(Column.DataType).Single().Name.Parts;
        var displayName = qualifiedType.Count > 1 && !IsBuiltIn(typeName)
            ? string.Join('.', qualifiedType.Select(part => $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"))
            : typeName.ToLowerInvariant();

        if (LengthTypes.Contains(typeName))
        {
            var length = column.GetProperty<int>(Column.Length);
            var argument = column.GetProperty<bool>(Column.IsMax) ? "max" : length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"{displayName}({argument})";
        }

        if (typeName.Equals("decimal", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("numeric", StringComparison.OrdinalIgnoreCase))
        {
            var precision = column.GetProperty<int>(Column.Precision);
            var scale = column.GetProperty<int>(Column.Scale);
            return $"{displayName}({precision},{scale})";
        }

        if (ScaleTypes.Contains(typeName))
            return $"{displayName}({column.GetProperty<int>(Column.Scale)})";

        return displayName;
    }

    private static bool IsBuiltIn(string typeName) => Enum.TryParse<SqlDataType>(typeName, true, out _);
}
