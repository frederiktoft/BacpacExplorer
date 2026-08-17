using BacpacExplorer.Core.Models;
using Microsoft.Data.SqlClient;
using BacpacExplorer.Core.Localization;

namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Reads bounded pages from a schema-selected table in the temporary LocalDB database.
/// It fetches one extra row to determine whether a next page exists and limits binary previews for display.
/// </summary>
public sealed class SqlDataBrowser(string connectionString) : IDataBrowser
{
    public async Task<DataPage> ReadPageAsync(
        DatabaseTable table,
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        if (pageSize > 1_000)
            throw new ArgumentOutOfRangeException(nameof(pageSize), CoreText.Get("PageSizeLimit"));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = SqlPageQuery.Build(table);
        command.Parameters.AddWithValue("@offset", offset);
        command.Parameters.AddWithValue("@fetch", pageSize + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyList<object?>>(pageSize + 1);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new object?[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
                values[index] = reader.IsDBNull(index) ? null : ToDisplayValue(reader.GetValue(index));

            rows.Add(values);
        }

        var hasNextPage = rows.Count > pageSize;
        if (hasNextPage)
            rows.RemoveAt(rows.Count - 1);

        return new DataPage(columns, rows, offset, pageSize, hasNextPage);
    }

    private static object ToDisplayValue(object value) => value switch
    {
        byte[] bytes when bytes.Length <= 64 => $"0x{Convert.ToHexString(bytes)}",
        byte[] bytes => CoreText.Format("BinaryPreview", $"0x{Convert.ToHexString(bytes.AsSpan(0, 64))}", bytes.Length),
        _ => value
    };
}
