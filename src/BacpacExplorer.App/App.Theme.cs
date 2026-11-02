using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace BacpacExplorer.App;

internal enum ThemePreference
{
    System,
    Light,
    Dark
}

public partial class App
{
    private static readonly string ThemePreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BacpacExplorer", "theme.txt");

    internal ThemePreference SelectedTheme { get; private set; } = ThemePreference.System;

    private void InitializeTheme()
    {
        try
        {
            if (File.Exists(ThemePreferencePath)
                && Enum.TryParse<ThemePreference>(File.ReadAllText(ThemePreferencePath).Trim(), out var preference)
                && Enum.IsDefined(preference))
                SelectedTheme = preference;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not read theme preference; following the Windows theme");
        }

        ApplyTheme();
    }

    internal void SetTheme(ThemePreference preference)
    {
        SelectedTheme = preference;
        ApplyTheme();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePreferencePath)!);
            File.WriteAllText(ThemePreferencePath, preference.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not save theme preference");
        }
    }

    private void ApplyTheme()
    {
        // WPF's built-in Fluent theme also tracks Windows changes in System mode.
#pragma warning disable WPF0001 // ThemeMode is experimental in .NET 10; keep its use isolated here.
        ThemeMode = SelectedTheme switch
        {
            ThemePreference.Light => System.Windows.ThemeMode.Light,
            ThemePreference.Dark => System.Windows.ThemeMode.Dark,
            _ => System.Windows.ThemeMode.System
        };
#pragma warning restore WPF0001
    }
}
