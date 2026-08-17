using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BacpacExplorer.Core.Tests;

public sealed class LocalizationReadinessTests
{
    [Fact]
    public void MainWindow_AllLocalizationKeysExistInNeutralResources()
    {
        var root = FindSolutionRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "BacpacExplorer.App", "MainWindow.xaml"));
        var resource = XDocument.Load(Path.Combine(root, "src", "BacpacExplorer.App", "Localization", "AppText.resx"));
        var resourceKeys = resource.Root!.Elements("data")
            .Select(element => (string)element.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);
        var usedKeys = Regex.Matches(xaml, @"\{loc:Loc\s+([A-Za-z0-9_]+)\}")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(usedKeys);
        Assert.Empty(usedKeys.Except(resourceKeys));
    }

    [Fact]
    public void NeutralResourceFiles_DoNotContainDuplicateKeys()
    {
        var root = FindSolutionRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "BacpacExplorer.App", "Localization", "AppText.resx"),
            Path.Combine(root, "src", "BacpacExplorer.Core", "Localization", "CoreText.resx")
        };

        foreach (var file in files)
        {
            var keys = XDocument.Load(file).Root!.Elements("data")
                .Select(element => (string)element.Attribute("name")!)
                .ToArray();
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void MainWindow_HasNoUnlocalizedUserFacingAttributes()
    {
        var root = FindSolutionRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "BacpacExplorer.App", "MainWindow.xaml"));
        var userFacingNames = new HashSet<string>(["Title", "Content", "Text", "Header", "ToolTip"], StringComparer.Ordinal);
        var literals = document.Descendants()
            .Attributes()
            .Where(attribute => userFacingNames.Contains(attribute.Name.LocalName))
            .Select(attribute => attribute.Value)
            .Where(value => !value.StartsWith('{') && !int.TryParse(value, out _))
            .ToArray();

        Assert.Empty(literals);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BacpacExplorer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the solution root from the test output directory.");
    }
}
