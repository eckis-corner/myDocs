using System.Xml.Linq;

namespace MyDocs.Tests;

/// <summary>Alle Sprachen müssen dieselben Schlüssel haben, kein Text darf leer sein.</summary>
public sealed class ResourceParityTests
{
    private static readonly string ResourceDirectory = Path.Combine(FindRepositoryRoot(), "src", "MyDocs.UI", "Resources");

    [Fact]
    public void GermanAndEnglishHaveTheSameKeys()
    {
        Assert.Equal(Load("SharedResource.resx").Keys.Order(), Load("SharedResource.en.resx").Keys.Order());
    }

    [Theory]
    [InlineData("SharedResource.resx")]
    [InlineData("SharedResource.en.resx")]
    public void NoTextIsEmpty(string file)
    {
        Assert.Empty(Load(file).Where(x => string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Key));
    }

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(ResourceDirectory, file))
            .Root!
            .Elements("data")
            .ToDictionary(x => (string)x.Attribute("name")!, x => (string?)x.Element("value") ?? string.Empty);

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyDocs.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository-Wurzel nicht gefunden.");
    }
}
