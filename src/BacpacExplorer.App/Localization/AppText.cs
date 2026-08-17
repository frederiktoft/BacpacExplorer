using System.Globalization;
using System.Resources;

namespace BacpacExplorer.App.Localization;

/// <summary>
/// Provides culture-aware access and formatting for WPF resource strings used from code-behind.
/// </summary>
public static class AppText
{
    private static readonly ResourceManager ResourceManager =
        new("BacpacExplorer.App.Localization.AppText", typeof(AppText).Assembly);

    public static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing UI resource '{key}'.");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
