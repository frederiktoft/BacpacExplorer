using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BacpacExplorer.App.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;

namespace BacpacExplorer.App;

/// <summary>
/// Owns application-wide logging startup, shutdown flushing, and last-chance exception handlers.
/// </summary>
public partial class App : Application
{
    internal static ILoggerFactory LoggerFactory { get; private set; } = NullLoggerFactory.Instance;
    private Microsoft.Extensions.Logging.ILogger<App> _logger = NullLogger<App>.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        var logging = AppLogging.Initialize();
        Log.Logger = logging.Logger;
        LoggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);
        _logger = LoggerFactory.CreateLogger<App>();

        DispatcherUnhandledException += LogDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
        TaskScheduler.UnobservedTaskException += LogUnobservedTaskException;

        if (logging.PortableDirectoryFailure is not null)
        {
            _logger.LogWarning(
                logging.PortableDirectoryFailure,
                "Portable logging beside the executable is unavailable; using {LogDirectory}",
                logging.LogDirectory);
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        _logger.LogInformation(
            "BACPAC Explorer starting. Version {Version}; logs are written to {LogDirectory}",
            version,
            logging.LogDirectory);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger.LogInformation("BACPAC Explorer shutting down with exit code {ExitCode}", e.ApplicationExitCode);

        DispatcherUnhandledException -= LogDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= LogUnhandledException;
        TaskScheduler.UnobservedTaskException -= LogUnobservedTaskException;
        LoggerFactory.Dispose();
        Log.CloseAndFlush();

        base.OnExit(e);
    }

    private void LogDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        _logger.LogCritical(e.Exception, "Unhandled exception on the WPF dispatcher");

    private void LogUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _logger.LogCritical(exception, "Unhandled application-domain exception. Terminating: {IsTerminating}", e.IsTerminating);
        else
            _logger.LogCritical("Unhandled non-Exception application-domain error. Terminating: {IsTerminating}; value: {Value}", e.IsTerminating, e.ExceptionObject);
    }

    private void LogUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _logger.LogError(e.Exception, "Unobserved task exception");
}
