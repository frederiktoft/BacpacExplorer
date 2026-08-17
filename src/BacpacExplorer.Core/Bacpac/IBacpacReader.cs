using BacpacExplorer.Core.Models;

namespace BacpacExplorer.Core.Bacpac;

/// <summary>
/// Defines the package-reading boundary used by the UI to obtain schema metadata without depending on DACFx.
/// </summary>
public interface IBacpacReader
{
    Task<BacpacDatabase> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
