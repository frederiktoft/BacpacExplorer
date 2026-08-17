using System.Globalization;
using System.Resources;

namespace BacpacExplorer.Core.Localization;

/// <summary>
/// Provides culture-aware access to Core resource strings without exposing generated resource classes to consumers.
/// </summary>
internal static class CoreText
{
    private static readonly ResourceManager ResourceManager =
        new("BacpacExplorer.Core.Localization.CoreText", typeof(CoreText).Assembly);

    public static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing Core resource '{key}'.");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
