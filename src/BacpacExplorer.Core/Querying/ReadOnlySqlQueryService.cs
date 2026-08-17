using System.Diagnostics;
using Microsoft.Data.SqlClient;
using BacpacExplorer.Core.Localization;

namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Executes validated SQL under a generated database user granted only SELECT permission.
/// It uses an unpooled connection so impersonation cannot leak between requests and applies timeout and row-count limits.
/// </summary>
public sealed class ReadOnlySqlQueryService(
    string connectionString,
    string readOnlyUserName,
    IReadOnlyQueryValidator? validator = null) : ISqlQueryService
{
    private const int MaximumRows = 1_000;
    private readonly IReadOnlyQueryValidator _validator = validator ?? new ScriptDomReadOnlyQueryValidator();
    private readonly string _queryConnectionString = BuildNonPooledConnectionString(connectionString);

    public async Task<QueryResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(sql);
        if (!validation.IsValid)
            throw new QueryExecutionException(string.Join(Environment.NewLine, validation.Errors), new InvalidOperationException(CoreText.Get("QueryValidationFailed")));

        var messages = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // EXECUTE AS changes session state. A dedicated physical connection ensures
            // the restricted context can never leak into a later pooled connection.
            await using var connection = new SqlConnection(_queryConnectionString);
            connection.InfoMessage += (_, args) => messages.Add(args.Message);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var impersonate = connection.CreateCommand())
            {
                impersonate.CommandText = $"EXECUTE AS USER = N'{EscapeSqlString(readOnlyUserName)}';";
                await impersonate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            command.CommandText = $"SET LOCK_TIMEOUT 10000; SET ROWCOUNT {MaximumRows + 1};{Environment.NewLine}{sql}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var rawColumnNames = Enumerable.Range(0, reader.FieldCount)
                .Select(reader.GetName)
                .ToArray();
            var columns = MakeUniqueColumnNames(rawColumnNames);
            var rows = new List<IReadOnlyList<object?>>(MaximumRows + 1);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new object?[reader.FieldCount];
                for (var index = 0; index < reader.FieldCount; index++)
                    values[index] = reader.IsDBNull(index) ? null : FormatValue(reader.GetValue(index));
                rows.Add(values);
            }

            var truncated = rows.Count > MaximumRows;
            if (truncated)
                rows.RemoveAt(rows.Count - 1);

            stopwatch.Stop();
            return new QueryResult(columns, rows, truncated, stopwatch.Elapsed, messages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            stopwatch.Stop();
            throw new QueryExecutionException(exception.Message, exception);
        }
    }

    internal static IReadOnlyList<string> MakeUniqueColumnNames(IReadOnlyList<string> rawColumnNames)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueNames = new string[rawColumnNames.Count];
        for (var index = 0; index < rawColumnNames.Count; index++)
        {
            var baseName = string.IsNullOrWhiteSpace(rawColumnNames[index])
                ? CoreText.Format("GeneratedColumnName", index + 1)
                : rawColumnNames[index];
            var candidate = baseName;
            var suffix = 2;
            while (!usedNames.Add(candidate))
                candidate = $"{baseName}_{suffix++}";

            uniqueNames[index] = candidate;
        }

        return uniqueNames;
    }

    private static object FormatValue(object value) => value switch
    {
        byte[] bytes when bytes.Length <= 64 => $"0x{Convert.ToHexString(bytes)}",
        byte[] bytes => CoreText.Format("BinaryPreview", $"0x{Convert.ToHexString(bytes.AsSpan(0, 64))}", bytes.Length),
        _ => value
    };

    private static string EscapeSqlString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string BuildNonPooledConnectionString(string value)
    {
        var builder = new SqlConnectionStringBuilder(value) { Pooling = false };
        return builder.ConnectionString;
    }
}
