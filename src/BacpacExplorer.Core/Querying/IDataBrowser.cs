using BacpacExplorer.Core.Models;

namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Defines bounded, read-only paging for tables selected from trusted BACPAC schema metadata.
/// </summary>
public interface IDataBrowser
{
    Task<DataPage> ReadPageAsync(
        DatabaseTable table,
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default);
}
