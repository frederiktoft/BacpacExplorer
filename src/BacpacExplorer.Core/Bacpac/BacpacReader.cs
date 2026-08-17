using System.IO.Compression;
using BacpacExplorer.Core.Models;
using BacpacExplorer.Core.Localization;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace BacpacExplorer.Core.Bacpac;

/// <summary>
/// Validates a BACPAC and reads its schema metadata into UI-independent application models.
/// Package work runs off the caller's thread because DACFx model loading is synchronous and can be expensive.
/// </summary>
public sealed class BacpacReader : IBacpacReader
{
    public Task<BacpacDatabase> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => ReadCore(filePath, cancellationToken), cancellationToken);
    }

    private static BacpacDatabase ReadCore(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(CoreText.Get("BacpacNotFound"), filePath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // BacPackage performs package-level validation and rejects DACPAC-only files.
            using (BacPackage.Load(filePath, DacSchemaModelStorageType.Memory))
            {
            }

            var entries = ReadPackageEntries(filePath, cancellationToken);
            var loadOptions = new ModelLoadOptions(DacSchemaModelStorageType.Memory, loadAsScriptBackedModel: false);

            using var model = TSqlModel.LoadFromDacpac(filePath, loadOptions, cancellationToken);
            var schemas = DacModelMapper.ReadSchemas(model, cancellationToken);
            var file = new FileInfo(filePath);

            return new BacpacDatabase(
                file.FullName,
                Path.GetFileNameWithoutExtension(file.Name),
                file.Length,
                schemas,
                entries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or DacModelException or DacServicesException)
        {
            throw new BacpacReadException(
                CoreText.Format("InvalidBacpac", Path.GetFileName(filePath), exception.Message),
                exception);
        }
    }

    private static IReadOnlyList<string> ReadPackageEntries(string filePath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(filePath);
        return archive.Entries
            .Select(entry =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return entry.FullName;
            })
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
