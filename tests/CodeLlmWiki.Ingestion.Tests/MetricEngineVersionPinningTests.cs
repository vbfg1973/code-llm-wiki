using System.Xml.Linq;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MetricEngineVersionPinningTests
{
    [Fact]
    public void PackageReferences_ArePinnedToExplicitVersions()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var projectFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectFiles);

        foreach (var projectFile in projectFiles)
        {
            var document = XDocument.Load(projectFile);
            var packageReferences = document
                .Descendants("PackageReference")
                .Select(element => new
                {
                    Include = element.Attribute("Include")?.Value ?? string.Empty,
                    Version = element.Attribute("Version")?.Value
                        ?? element.Element("Version")?.Value
                        ?? string.Empty,
                })
                .ToArray();

            foreach (var packageReference in packageReferences)
            {
                Assert.False(string.IsNullOrWhiteSpace(packageReference.Version), $"Package '{packageReference.Include}' in '{projectFile}' must specify an explicit version.");
                Assert.DoesNotContain("*", packageReference.Version);
                Assert.DoesNotContain("[", packageReference.Version);
                Assert.DoesNotContain("]", packageReference.Version);
                Assert.DoesNotContain(",", packageReference.Version);
                Assert.Matches(@"^\d+\.\d+\.\d+([\-+][0-9A-Za-z\.-]+)?$", packageReference.Version);
            }
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src"))
                && Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved.");
    }
}
