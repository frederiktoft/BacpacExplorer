using BacpacExplorer.Core.Bacpac;

namespace BacpacExplorer.Core.Tests;

public sealed class BacpacReaderTests
{
    [Fact]
    public async Task ReadAsync_WrapsMalformedPackageErrorsWithUsefulContext()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bacpac");
        await File.WriteAllTextAsync(filePath, "not a package");

        try
        {
            var exception = await Assert.ThrowsAsync<BacpacReadException>(() => new BacpacReader().ReadAsync(filePath));

            Assert.Contains(Path.GetFileName(filePath), exception.Message);
            Assert.NotNull(exception.InnerException);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
