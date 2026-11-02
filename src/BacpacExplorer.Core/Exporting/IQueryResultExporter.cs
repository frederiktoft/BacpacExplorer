using BacpacExplorer.Core.Querying;

namespace BacpacExplorer.Core.Exporting;

/// <summary>
/// Writes loaded query results in one format without executing SQL or owning the destination stream.
/// </summary>
public interface IQueryResultExporter
{
    string FileExtension { get; }

    void Write(QueryResult result, Stream destination);
}
