using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class EndpointControllerVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesControllerEndpoints_WithTraceabilityAndRuleProvenance()
    {
        var fixture = await EndpointFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var controllerType = model.Declarations.Types.Single(x => x.Name == "OrdersController");
        var getMethod = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == controllerType.Id && x.Name == "GetById");

        var endpoint = Assert.Single(model.Endpoints.Endpoints, x => x.DeclaringMethodId == getMethod.Id);
        Assert.Equal("controller-action", endpoint.Kind);
        Assert.Equal("controller", endpoint.Family);
        Assert.Equal("GET", endpoint.HttpMethod);
        Assert.Equal(EndpointConfidence.High, endpoint.Confidence);
        Assert.Equal("aspnetcore.controller.attribute-route", endpoint.RuleId);
        Assert.Equal("1", endpoint.RuleVersion);
        Assert.Equal("code-defined", endpoint.RuleSource);
        Assert.Equal("api/orders/{id}", endpoint.NormalizedRouteKey);
        Assert.Equal(controllerType.Id, endpoint.DeclaringTypeId!.Value);
        Assert.NotNull(endpoint.NamespaceId);
        Assert.NotNull(endpoint.GroupId);
        Assert.NotEmpty(endpoint.DeclarationFileIds);

        var endpointGroup = Assert.Single(model.Endpoints.Groups, x => x.Id == endpoint.GroupId!.Value);
        Assert.Equal("controller", endpointGroup.Family);
        Assert.Equal(controllerType.Id, endpointGroup.DeclaringTypeId!.Value);
        Assert.Contains(endpoint.Id, endpointGroup.EndpointIds);
    }

    [Fact]
    public async Task Render_EmitsEndpointPage_WithNamespaceTypeMethodAndFileLinks()
    {
        var fixture = await EndpointFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var endpointPage = pages.Single(x =>
            x.RelativePath.StartsWith("endpoints/controller/", StringComparison.Ordinal)
            && x.Markdown.Contains("entity_type: endpoint", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_http_method: GET", StringComparison.Ordinal));

        Assert.Contains("## Declaration Traceability", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Namespace: [[namespaces/", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Declaring Type: [[types/", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Declaring Method: [[methods/", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Declaration Files", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- [[files/", endpointPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_CapturesDistinctEndpointsPerControllerRoutePrefix()
    {
        var fixture = await MultiRouteEndpointFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var controllerType = model.Declarations.Types.Single(x => x.Name == "OrdersController");
        var getMethod = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == controllerType.Id && x.Name == "GetById");
        var endpoints = model.Endpoints.Endpoints
            .Where(x => x.DeclaringMethodId == getMethod.Id)
            .ToArray();

        Assert.Equal(2, endpoints.Length);
        Assert.Equal(2, endpoints.Select(x => x.CanonicalSignature).Distinct().Count());
        Assert.Contains(endpoints, x => x.NormalizedRouteKey == "api/orders/{id}");
        Assert.Contains(endpoints, x => x.NormalizedRouteKey == "v2/orders/{id}");
        Assert.Equal(2, endpoints.Select(x => x.GroupId).Distinct().Count());

        var endpointGroupIds = endpoints
            .Where(x => x.GroupId is not null)
            .Select(x => x.GroupId!.Value)
            .Distinct()
            .ToHashSet();
        var groups = model.Endpoints.Groups
            .Where(x => endpointGroupIds.Contains(x.Id))
            .ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Contains(groups, x => x.NormalizedRoutePrefix == "api/orders");
        Assert.Contains(groups, x => x.NormalizedRoutePrefix == "v2/orders");
    }

    private sealed class EndpointFixture
    {
        private EndpointFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<EndpointFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-endpoints-{Guid.NewGuid():N}", "endpoint-repo");
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
                    <AssemblyName>Acme.Endpoints.Web</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(webDir, "OrdersController.cs"),
                """
                using Microsoft.AspNetCore.Mvc;

                namespace Acme.Endpoints;

                [ApiController]
                [Route("api/[controller]")]
                public sealed class OrdersController : ControllerBase
                {
                    [HttpGet("{id}")]
                    public IActionResult GetById(string id)
                    {
                        return Ok(id);
                    }

                    [HttpPost]
                    public IActionResult Create()
                    {
                        return Accepted();
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new EndpointFixture(root);
        }
    }

    private sealed class MultiRouteEndpointFixture
    {
        private MultiRouteEndpointFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MultiRouteEndpointFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-endpoints-multi-{Guid.NewGuid():N}", "endpoint-repo");
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
                    <AssemblyName>Acme.Endpoints.Web</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(webDir, "OrdersController.cs"),
                """
                using Microsoft.AspNetCore.Mvc;

                namespace Acme.Endpoints;

                [ApiController]
                [Route("api/[controller]")]
                [Route("v2/[controller]")]
                public sealed class OrdersController : ControllerBase
                {
                    [HttpGet("{id}")]
                    public IActionResult GetById(string id)
                    {
                        return Ok(id);
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MultiRouteEndpointFixture(root);
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
