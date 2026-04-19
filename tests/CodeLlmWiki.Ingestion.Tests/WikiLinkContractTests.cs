using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class WikiLinkContractTests
{
    [Fact]
    public void ToMarkdownLink_UsesParseSafeMarkdownSyntax_ForTables()
    {
        var resolver = new WikiPathResolver();
        var project = new ProjectNode(
            new EntityId("entity:project:sample"),
            "Sample.Project",
            "src/Sample.Project/Sample.Project.csproj",
            "slnx",
            [],
            []);
        resolver.RegisterProject(project);

        var link = resolver.ToMarkdownLink(project.Id, "Sample|Project");

        Assert.Equal("[Sample\\|Project](projects/Sample.Project.md)", link);
    }

    [Fact]
    public void BuildPackageExternalTypeAnchor_IsDeterministic_AndStable()
    {
        var first = WikiPathResolver.BuildPackageExternalTypeAnchor("newtonsoft.json", "Newtonsoft.Json.Linq.JToken");
        var second = WikiPathResolver.BuildPackageExternalTypeAnchor("newtonsoft.json", "Newtonsoft.Json.Linq.JToken");

        Assert.Equal(first, second);
        Assert.StartsWith("ext-newtonsoft-json-linq-jtoken-", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPackageExternalTypeMarkdownLink_RoutesToPackagePageDeepAnchor()
    {
        var resolver = new WikiPathResolver();
        var package = new PackageNode(
            new EntityId("entity:package:newtonsoft-json"),
            "Newtonsoft.Json",
            "newtonsoft.json",
            [],
            [],
            []);
        resolver.RegisterPackage(package);

        var link = resolver.ToPackageExternalTypeMarkdownLink(
            package.Id,
            package.CanonicalKey,
            "Newtonsoft.Json.Linq.JToken",
            "Newtonsoft.Json.Linq.JToken");

        Assert.StartsWith("[Newtonsoft.Json.Linq.JToken](packages/Newtonsoft.Json.md#ext-newtonsoft-json-linq-jtoken-", link, StringComparison.Ordinal);
    }
}
