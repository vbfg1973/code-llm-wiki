using System.Diagnostics;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class NamespaceVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_EmitsNamespaceAndTypeContainmentTriples()
    {
        var fixture = await NamespaceFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        Assert.Contains(analysis.Triples, x => x.Predicate == CorePredicates.ContainsNamespace);
        Assert.Contains(analysis.Triples, x => x.Predicate == CorePredicates.ContainsType);
    }

    [Fact]
    public async Task Query_ProjectsDeterministicNamespaceHierarchy_AndContainedTypes()
    {
        var fixture = await NamespaceFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var namespaces = model.Declarations.Namespaces;
        Assert.NotEmpty(namespaces);

        var acme = namespaces.Single(x => x.Name == "Acme");
        var payments = namespaces.Single(x => x.Name == "Acme.Payments");
        var services = namespaces.Single(x => x.Name == "Acme.Payments.Services");
        var contracts = namespaces.Single(x => x.Name == "Acme.Payments.Contracts");

        Assert.Null(acme.ParentNamespaceId);
        Assert.Equal(acme.Id, payments.ParentNamespaceId);
        Assert.Equal(payments.Id, services.ParentNamespaceId);
        Assert.Equal(payments.Id, contracts.ParentNamespaceId);

        var containedTypeNames = model.Declarations.Types
            .Where(x => x.NamespaceId == services.Id)
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["PaymentService"], containedTypeNames);
    }

    [Fact]
    public async Task Render_NamespacePages_AreHierarchicalAndDeterministic_WithMinimalFrontMatter()
    {
        var fixture = await NamespaceFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);

        Assert.Contains(pages, x => x.RelativePath == "namespaces/Acme.md");
        Assert.Contains(pages, x => x.RelativePath == "namespaces/Acme/Payments.md");
        Assert.Contains(pages, x => x.RelativePath == "namespaces/Acme/Payments/Services.md");

        var servicesPage = pages.Single(x => x.RelativePath == "namespaces/Acme/Payments/Services.md");
        Assert.Contains("entity_type: namespace", servicesPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("namespace_name: Acme.Payments.Services", servicesPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("parent_namespace_id:", servicesPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Contained Types", servicesPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("PaymentService", servicesPage.Markdown, StringComparison.Ordinal);

        var rootPage = pages.Single(x => x.RelativePath == "namespaces/Acme.md");
        Assert.DoesNotContain("parent_namespace_id:", rootPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_NamespacePathCollisions_UseDeterministicSuffixing()
    {
        var fixture = await NamespaceCollisionFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model)
            .Where(x => x.RelativePath.StartsWith("namespaces/", StringComparison.Ordinal))
            .Select(x => x.RelativePath)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("namespaces/Acme/Payments.md", pages);
        Assert.Contains("namespaces/acme/payments--2.md", pages);
    }

    [Fact]
    public async Task Query_RepresentsGlobalNamespaceTypes_WhenNoNamespaceDeclarationExists()
    {
        var fixture = await GlobalNamespaceFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var globalNamespace = model.Declarations.Namespaces.Single(x => x.Name == "<global>");
        var globalTypes = model.Declarations.Types
            .Where(x => x.NamespaceId == globalNamespace.Id)
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["StandaloneType"], globalTypes);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        Assert.Contains(pages, x => x.RelativePath == "namespaces/global.md");
    }

    private sealed class NamespaceFixture
    {
        private NamespaceFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<NamespaceFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-namespace-{Guid.NewGuid():N}", "namespace-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(appDir);
            await File.WriteAllTextAsync(Path.Combine(appDir, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "PaymentService.cs"),
                """
                namespace Acme.Payments.Services;

                public sealed class PaymentService { }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Gateway.cs"),
                """
                namespace Acme.Payments.Contracts;

                public interface IPaymentGateway { }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new NamespaceFixture(root);
        }
    }

    private sealed class NamespaceCollisionFixture
    {
        private NamespaceCollisionFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<NamespaceCollisionFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-namespace-collision-{Guid.NewGuid():N}", "namespace-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(appDir);
            await File.WriteAllTextAsync(Path.Combine(appDir, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "One.cs"),
                """
                namespace Acme.Payments;
                public sealed class One { }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Two.cs"),
                """
                namespace acme.payments;
                public sealed class Two { }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new NamespaceCollisionFixture(root);
        }
    }

    private sealed class GlobalNamespaceFixture
    {
        private GlobalNamespaceFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<GlobalNamespaceFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-global-namespace-{Guid.NewGuid():N}", "global-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(appDir);
            await File.WriteAllTextAsync(Path.Combine(appDir, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "StandaloneType.cs"),
                """
                public sealed class StandaloneType { }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new GlobalNamespaceFixture(root);
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stdOut}\n{stdErr}");
        }
    }
}
