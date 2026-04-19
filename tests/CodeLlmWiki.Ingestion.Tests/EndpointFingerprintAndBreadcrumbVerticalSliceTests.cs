using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class EndpointFingerprintAndBreadcrumbVerticalSliceTests
{
    [Fact]
    public async Task Render_EndpointPage_EmitsFingerprintAndContextTaggedBreadcrumbs()
    {
        var fixture = await EndpointBreadcrumbFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var endpointPage = pages.Single(x =>
            x.RelativePath.StartsWith("endpoints/controller/", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_http_method: POST", StringComparison.Ordinal));

        Assert.Contains("endpoint_fingerprint:", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Matchability Fingerprint", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- fingerprint_hash: `", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Outbound Breadcrumbs (Bounded)", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[context=declaration]", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[context=method-body] calls [[methods/", endpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[context=method-body] calls external", endpointPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_EndpointFingerprint_IsDeterministicAcrossRuns()
    {
        var fixture = await EndpointBreadcrumbFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var firstAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var secondAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var firstModel = new ProjectStructureQueryService(firstAnalysis.Triples).GetModel(firstAnalysis.RepositoryId);
        var secondModel = new ProjectStructureQueryService(secondAnalysis.Triples).GetModel(secondAnalysis.RepositoryId);
        var firstPage = new ProjectStructureWikiRenderer().Render(firstModel).Single(x =>
            x.RelativePath.StartsWith("endpoints/controller/", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_http_method: POST", StringComparison.Ordinal));
        var secondPage = new ProjectStructureWikiRenderer().Render(secondModel).Single(x =>
            x.RelativePath.StartsWith("endpoints/controller/", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_http_method: POST", StringComparison.Ordinal));

        var firstFingerprint = ExtractFrontMatterValue(firstPage.Markdown, "endpoint_fingerprint");
        var secondFingerprint = ExtractFrontMatterValue(secondPage.Markdown, "endpoint_fingerprint");
        Assert.Equal(firstFingerprint, secondFingerprint);
    }

    private static string ExtractFrontMatterValue(string markdown, string key)
    {
        var keyPrefix = $"{key}: ";
        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                return line[keyPrefix.Length..].Trim();
            }
        }

        return string.Empty;
    }

    private sealed class EndpointBreadcrumbFixture
    {
        private EndpointBreadcrumbFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<EndpointBreadcrumbFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-endpoint-breadcrumb-{Guid.NewGuid():N}", "endpoint-breadcrumb-repo");
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
                    <AssemblyName>Acme.EndpointBreadcrumb.Web</AssemblyName>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="Serilog" Version="4.2.0" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(webDir, "OrdersController.cs"),
                """
                using Microsoft.AspNetCore.Mvc;

                namespace Acme.EndpointBreadcrumb;

                [ApiController]
                [Route("api/[controller]")]
                public sealed class OrdersController : ControllerBase
                {
                    [HttpPost]
                    public IActionResult Create([FromBody] string payload, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
                    {
                        Serilog.Log.Information("received {payload}", payload);
                        return Ok(Dispatch(payload));
                    }

                    private string Dispatch(string value)
                    {
                        return value;
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new EndpointBreadcrumbFixture(root);
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
