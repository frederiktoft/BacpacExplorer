namespace BacpacExplorer.Core.Bacpac;

/// <summary>
/// Represents a package validation or schema-reading failure while preserving the lower-level DACFx or I/O exception.
/// </summary>
public sealed class BacpacReadException : Exception
{
    public BacpacReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
