using System.Globalization;
using System.IO;
using System.Security;
using Serilog;

namespace BacpacExplorer.App.Logging;

/// <summary>
/// Configures Serilog as the file provider behind Microsoft.Extensions.Logging.
/// It prefers portable executable-relative logs, verifies that location with a write probe,
/// and falls back to the current user's LocalAppData directory when necessary.
/// </summary>
internal static class AppLogging
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;

    public static LoggingInitialization Initialize()
    {
        var portableDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        if (TryCreateLogger(portableDirectory, out var portableLogger, out var portableFailure))
            return new LoggingInitialization(portableLogger!, portableDirectory, null);

        var fallbackDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BacpacExplorer",
            "Logs");
        Directory.CreateDirectory(fallbackDirectory);
        return new LoggingInitialization(CreateLogger(fallbackDirectory), fallbackDirectory, portableFailure);
    }

    private static bool TryCreateLogger(
        string logDirectory,
        out Serilog.ILogger? logger,
        out Exception? failure)
    {
        logger = null;
        if (!TryPrepareDirectory(logDirectory, out failure))
            return false;

        try
        {
            VerifyCurrentLogFileCanBeOpened(logDirectory);
            logger = CreateLogger(logDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or SecurityException
                                           or NotSupportedException
                                           or InvalidOperationException)
        {
            failure = exception;
            return false;
        }
    }

    private static void VerifyCurrentLogFileCanBeOpened(string logDirectory)
    {
        var currentLogPath = Path.Combine(
            logDirectory,
            $"bacpac-explorer-{DateTime.Now:yyyyMMdd}.log");
        using var stream = new FileStream(
            currentLogPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1);
    }

    private static Serilog.ILogger CreateLogger(string logDirectory) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "bacpac-explorer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: true,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

    private static bool TryPrepareDirectory(string directory, out Exception? failure)
    {
        var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            failure = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            failure = exception;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // A DeleteOnClose stream normally removes the probe. Failure to repeat
                // that cleanup must not prevent the application from starting.
            }
        }
    }
}

/// <summary>
/// Returns the configured provider together with its selected path and any recoverable portable-path failure.
/// </summary>
internal sealed record LoggingInitialization(
    Serilog.ILogger Logger,
    string LogDirectory,
    Exception? PortableDirectoryFailure);
