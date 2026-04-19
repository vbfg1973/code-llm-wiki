using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MinimalApiEndpointVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesMinimalApiEndpoints_WithGroupPrefixComposition()
    {
        var fixture = await MinimalApiFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var minimalEndpoints = model.Endpoints.Endpoints
            .Where(x => x.Family == "minimal-api")
            .ToArray();

        Assert.Equal(2, minimalEndpoints.Length);
        var getEndpoint = Assert.Single(minimalEndpoints, x => x.HttpMethod == "GET");
        Assert.Equal("minimal-api-route", getEndpoint.Kind);
        Assert.Equal("api/v1/orders/{id}", getEndpoint.NormalizedRouteKey);
        Assert.Equal("aspnetcore.minimalapi.map", getEndpoint.RuleId);
        Assert.Equal(EndpointConfidence.High, getEndpoint.Confidence);
        Assert.NotNull(getEndpoint.GroupId);

        var postEndpoint = Assert.Single(minimalEndpoints, x => x.HttpMethod == "POST");
        Assert.Equal("orders", postEndpoint.NormalizedRouteKey);
        Assert.Null(postEndpoint.GroupId);

        var endpointGroup = Assert.Single(model.Endpoints.Groups, x => x.Id == getEndpoint.GroupId!.Value);
        Assert.Equal("minimal-api", endpointGroup.Family);
        Assert.Equal("api/v1", endpointGroup.NormalizedRoutePrefix);
        Assert.Contains(getEndpoint.Id, endpointGroup.EndpointIds);
    }

    [Fact]
    public async Task Render_EmitsMinimalApiEndpointPages_InFamilyPath()
    {
        var fixture = await MinimalApiFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var endpointPage = pages.Single(x =>
            x.RelativePath.StartsWith("endpoints/minimal-api/", StringComparison.Ordinal)
            && x.Markdown.Contains("entity_type: endpoint", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_family: minimal-api", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_http_method: GET", StringComparison.Ordinal));

        Assert.Contains("## Declaration Traceability", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Endpoint Group: [[endpoints/minimal-api/groups/", endpointPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_AndRender_AreDeterministic_ForMinimalApiEndpoints()
    {
        var fixture = await MinimalApiFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var firstAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var secondAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var firstModel = new ProjectStructureQueryService(firstAnalysis.Triples).GetModel(firstAnalysis.RepositoryId);
        var secondModel = new ProjectStructureQueryService(secondAnalysis.Triples).GetModel(secondAnalysis.RepositoryId);

        Assert.Equal(
            firstModel.Endpoints.Endpoints.Select(x => x.Id.Value).ToArray(),
            secondModel.Endpoints.Endpoints.Select(x => x.Id.Value).ToArray());
        Assert.Equal(
            firstModel.Endpoints.Groups.Select(x => x.Id.Value).ToArray(),
            secondModel.Endpoints.Groups.Select(x => x.Id.Value).ToArray());

        var firstPages = new ProjectStructureWikiRenderer().Render(firstModel)
            .Where(x => x.RelativePath.StartsWith("endpoints/minimal-api/", StringComparison.Ordinal))
            .Select(x => (x.RelativePath, x.Markdown))
            .ToArray();
        var secondPages = new ProjectStructureWikiRenderer().Render(secondModel)
            .Where(x => x.RelativePath.StartsWith("endpoints/minimal-api/", StringComparison.Ordinal))
            .Select(x => (x.RelativePath, x.Markdown))
            .ToArray();

        Assert.Equal(firstPages, secondPages);
    }

    private sealed class MinimalApiFixture
    {
        private MinimalApiFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MinimalApiFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-minimalapi-{Guid.NewGuid():N}", "minimalapi-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/Web/Web.csproj" />
                </Solution>
                """);

            var webDir = Path.Combine(root, "src", "Web");
            Directory.CreateDirectory(webDir);
            await File.WriteAllTextAsync(Path.Combine(webDir, "Web.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.MinimalApi.Web</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(webDir, "Program.cs"),
                """
                var builder = WebApplication.CreateBuilder(args);
                var app = builder.Build();

                var api = app.MapGroup("/api");
                var v1 = api.MapGroup("/v1");

                v1.MapGet("/orders/{id}", (string id) => Results.Ok(id));
                app.MapPost("/orders", () => Results.Accepted());

                app.Run();
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MinimalApiFixture(root);
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
