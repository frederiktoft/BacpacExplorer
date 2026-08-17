using System.Windows.Markup;

namespace BacpacExplorer.App.Localization;

/// <summary>
/// Resolves a resource key from XAML so visible WPF text stays in the application's neutral RESX file.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => AppText.Get(Key);
}
