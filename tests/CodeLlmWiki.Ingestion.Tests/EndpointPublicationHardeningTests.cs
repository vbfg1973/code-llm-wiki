using System.Diagnostics;
using System.Text;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class EndpointPublicationHardeningTests
{
    [Fact]
    public async Task Render_EndpointPublication_IsDeterministicAndFamilyGroupedAcrossRuns()
    {
        var fixture = await EndpointPublicationFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var firstAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var secondAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var firstModel = new ProjectStructureQueryService(firstAnalysis.Triples).GetModel(firstAnalysis.RepositoryId);
        var secondModel = new ProjectStructureQueryService(secondAnalysis.Triples).GetModel(secondAnalysis.RepositoryId);
        var firstPages = new ProjectStructureWikiRenderer().Render(firstModel);
        var secondPages = new ProjectStructureWikiRenderer().Render(secondModel);

        var firstEndpointPublication = firstPages
            .Where(x => x.RelativePath.StartsWith("endpoints/", StringComparison.Ordinal) || x.RelativePath == "index/repository-index.md")
            .Select(x => (x.RelativePath, x.Markdown))
            .ToArray();
        var secondEndpointPublication = secondPages
            .Where(x => x.RelativePath.StartsWith("endpoints/", StringComparison.Ordinal) || x.RelativePath == "index/repository-index.md")
            .Select(x => (x.RelativePath, x.Markdown))
            .ToArray();
        Assert.Equal(firstEndpointPublication, secondEndpointPublication);

        var endpointPages = firstPages.Where(x =>
            x.RelativePath.StartsWith("endpoints/", StringComparison.Ordinal)
            && !x.RelativePath.Contains("/groups/", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(endpointPages);

        foreach (var endpointPage in endpointPages)
        {
            var segments = endpointPage.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(segments.Length >= 3, $"Unexpected endpoint path: {endpointPage.RelativePath}");
            var family = segments[1];
            Assert.Contains($"endpoint_family: {family}", endpointPage.Markdown, StringComparison.Ordinal);
        }

        var groupPages = firstPages.Where(x => x.RelativePath.Contains("/groups/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(groupPages);
        foreach (var groupPage in groupPages)
        {
            var segments = groupPage.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(segments.Length >= 4, $"Unexpected endpoint group path: {groupPage.RelativePath}");
            var family = segments[1];
            Assert.Contains($"endpoint_family: {family}", groupPage.Markdown, StringComparison.Ordinal);
        }

        var indexPage = firstPages.Single(x => x.RelativePath == "index/repository-index.md");
        Assert.Contains("## Endpoint Groups", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Endpoints", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Endpoint Diagnostics", indexPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndpointPipeline_StaysWithinPerformanceBudget_OnRepresentativeFixture()
    {
        var fixture = await EndpointPerformanceFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var analyzeWatch = Stopwatch.StartNew();
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        analyzeWatch.Stop();

        var projectWatch = Stopwatch.StartNew();
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);
        projectWatch.Stop();

        var endpointPageCount = pages.Count(x => x.RelativePath.StartsWith("endpoints/", StringComparison.Ordinal));
        Assert.True(endpointPageCount >= 80, $"Expected representative endpoint volume, got {endpointPageCount}.");

        Assert.True(analyzeWatch.ElapsedMilliseconds < 20_000, $"Endpoint extraction exceeded budget: {analyzeWatch.ElapsedMilliseconds}ms");
        Assert.True(projectWatch.ElapsedMilliseconds < 15_000, $"Endpoint publication exceeded budget: {projectWatch.ElapsedMilliseconds}ms");
    }

    private sealed class EndpointPublicationFixture
    {
        private EndpointPublicationFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<EndpointPublicationFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-endpoint-publication-{Guid.NewGuid():N}", "endpoint-pub-repo");
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
                    <AssemblyName>Acme.EndpointHardening</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="CommandLineParser" Version="2.9.1" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Controller.cs"),
                """
                using Microsoft.AspNetCore.Mvc;

                namespace Acme.EndpointHardening;

                [ApiController]
                [Route("api/[controller]")]
                public sealed class OrdersController : ControllerBase
                {
                    [HttpGet("{id}")]
                    public IActionResult Get(string id)
                    {
                        return Ok(id);
                    }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "MinimalApi.cs"),
                """
                var app = WebApplication.CreateBuilder(args).Build();
                app.MapPost("/orders", () => Results.Accepted());
                app.Run();
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Handlers.cs"),
                """
                namespace Acme.EndpointHardening.Messages;

                public sealed record Ping(string Value);

                public interface IMessageHandler<T>
                {
                    void Handle(T message);
                }

                public sealed class PingHandler : IMessageHandler<Ping>
                {
                    public void Handle(Ping message)
                    {
                    }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Cli.cs"),
                """
                using CommandLine;

                namespace Acme.EndpointHardening.Cli;

                [Verb("sync")]
                public sealed class SyncOptions
                {
                    public int Execute()
                    {
                        return 0;
                    }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Grpc.cs"),
                """
                namespace Acme.EndpointHardening.Grpc;

                public sealed class KnownGrpcService
                {
                    public void Handle()
                    {
                    }
                }

                public static class GrpcBootstrap
                {
                    public static void Register(WebApplication app)
                    {
                        app.MapGrpcService<KnownGrpcService>();
                        app.MapGrpcService<MissingGrpcService>();
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new EndpointPublicationFixture(root);
        }
    }

    private sealed class EndpointPerformanceFixture
    {
        private EndpointPerformanceFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<EndpointPerformanceFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-endpoint-perf-{Guid.NewGuid():N}", "endpoint-perf-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Perf.slnx"),
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
                    <AssemblyName>Acme.EndpointPerf</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="CommandLineParser" Version="2.9.1" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "GeneratedControllers.cs"), BuildControllerSource(40));
            await File.WriteAllTextAsync(Path.Combine(appDir, "GeneratedMinimal.cs"), BuildMinimalSource(40));
            await File.WriteAllTextAsync(Path.Combine(appDir, "GeneratedHandlers.cs"), BuildHandlerSource(40));
            await File.WriteAllTextAsync(Path.Combine(appDir, "GeneratedCli.cs"), BuildCliSource(10));
            await File.WriteAllTextAsync(Path.Combine(appDir, "GeneratedGrpc.cs"), BuildGrpcSource(10));

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new EndpointPerformanceFixture(root);
        }

        private static string BuildControllerSource(int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("namespace Acme.EndpointPerf.Controllers;");
            sb.AppendLine();

            for (var i = 0; i < count; i++)
            {
                sb.AppendLine("[ApiController]");
                sb.AppendLine($"[Route(\"api/controller{i}\")]");
                sb.AppendLine($"public sealed class Controller{i} : ControllerBase");
                sb.AppendLine("{");
                sb.AppendLine("    [HttpGet(\"{id}\")]");
                sb.AppendLine($"    public IActionResult Get(string id) => Ok(id + \"-{i}\");");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildMinimalSource(int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine("var app = WebApplication.CreateBuilder(args).Build();");
            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"app.MapGet(\"/m{i}/{{id}}\", (string id) => Results.Ok(id));");
            }

            sb.AppendLine("app.Run();");
            return sb.ToString();
        }

        private static string BuildHandlerSource(int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine("namespace Acme.EndpointPerf.Handlers;");
            sb.AppendLine("public interface IMessageHandler<T> { void Handle(T message); }");
            sb.AppendLine();

            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"public sealed record Ping{i}(string Value);");
                sb.AppendLine($"public sealed class Handler{i} : IMessageHandler<Ping{i}>");
                sb.AppendLine("{");
                sb.AppendLine($"    public void Handle(Ping{i} message) {{ }}");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildCliSource(int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using CommandLine;");
            sb.AppendLine("namespace Acme.EndpointPerf.Cli;");
            sb.AppendLine();

            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"[Verb(\"task{i}\")]");
                sb.AppendLine($"public sealed class Task{i}Options");
                sb.AppendLine("{");
                sb.AppendLine("    public int Execute() => 0;");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildGrpcSource(int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine("namespace Acme.EndpointPerf.Grpc;");
            sb.AppendLine();

            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"public sealed class GrpcService{i}");
                sb.AppendLine("{");
                sb.AppendLine("    public void Handle() { }");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("public static class GrpcMap");
            sb.AppendLine("{");
            sb.AppendLine("    public static void Register(WebApplication app)");
            sb.AppendLine("    {");
            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"        app.MapGrpcService<GrpcService{i}>();");
            }

            sb.AppendLine("        app.MapGrpcService<MissingGrpcService>();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start git process.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }
}
