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
    internal bool UseMicaBackground { get; private set; }

    private void InitializeTheme()
    {
        try
        {
            if (File.Exists(ThemePreferencePath))
            {
                var lines = File.ReadAllLines(ThemePreferencePath);
                if (lines.Length > 0
                    && Enum.TryParse<ThemePreference>(lines[0].Trim(), out var preference)
                    && Enum.IsDefined(preference))
                    SelectedTheme = preference;
                if (lines.Length > 1 && bool.TryParse(lines[1], out var useMica))
                    UseMicaBackground = useMica;
            }
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
        SaveThemePreference();
    }

    internal void SetMicaBackground(bool enabled)
    {
        UseMicaBackground = enabled;
        SaveThemePreference();
    }

    private void SaveThemePreference()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePreferencePath)!);
            File.WriteAllLines(ThemePreferencePath, [SelectedTheme.ToString(), UseMicaBackground.ToString()]);
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
