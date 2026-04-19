using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class GrpcEndpointVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesGrpcEndpoints_WithResolutionSemantics()
    {
        var fixture = await GrpcFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var grpcEndpoints = model.Endpoints.Endpoints.Where(x => x.Family == "grpc").ToArray();
        Assert.Equal(2, grpcEndpoints.Length);

        var resolvedEndpoint = Assert.Single(grpcEndpoints, x => x.Kind == "grpc-service-method");
        Assert.Equal(EndpointConfidence.High, resolvedEndpoint.Confidence);
        Assert.True(string.IsNullOrWhiteSpace(resolvedEndpoint.ResolutionReason));
        Assert.NotNull(resolvedEndpoint.DeclaringTypeId);
        Assert.NotNull(resolvedEndpoint.DeclaringMethodId);
        Assert.Equal("grpc/ordergrpcservice/getorder", resolvedEndpoint.NormalizedRouteKey);

        var unresolvedEndpoint = Assert.Single(grpcEndpoints, x => x.Kind == "grpc-service-unresolved");
        Assert.Equal(EndpointConfidence.Low, unresolvedEndpoint.Confidence);
        Assert.Equal("grpc-service-type-unresolved", unresolvedEndpoint.ResolutionReason);
        Assert.NotNull(unresolvedEndpoint.DeclaringTypeId);

        var diagnostic = Assert.Single(model.Endpoints.Diagnostics, x =>
            x.Family == "grpc" && x.Reason == "grpc-service-type-unresolved");
        Assert.Equal(1, diagnostic.Count);
    }

    [Fact]
    public async Task Render_GrpcEndpointDiagnostics_AppearOnEndpointAndIndexPages()
    {
        var fixture = await GrpcFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var unresolvedEndpointPage = pages.Single(x =>
            x.RelativePath.StartsWith("endpoints/grpc/", StringComparison.Ordinal)
            && x.Markdown.Contains("endpoint_resolution_reason: grpc-service-type-unresolved", StringComparison.Ordinal));
        Assert.Contains("- Resolution Reason: `grpc-service-type-unresolved`", unresolvedEndpointPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Declaring Type: `unresolved-type-reference:", unresolvedEndpointPage.Markdown, StringComparison.Ordinal);

        var indexPage = pages.Single(x => x.RelativePath == "index/repository-index.md");
        Assert.Contains("## Endpoint Diagnostics", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("grpc:grpc-service-type-unresolved", indexPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class GrpcFixture
    {
        private GrpcFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<GrpcFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-grpc-{Guid.NewGuid():N}", "grpc-repo");
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
                    <AssemblyName>Acme.Grpc.Web</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(webDir, "Program.cs"),
                """
                using Acme.Grpc;

                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddGrpc();

                var app = builder.Build();
                app.MapGrpcService<OrderGrpcService>();
                app.MapGrpcService<MissingGrpcService>();
                app.Run();
                """);

            await File.WriteAllTextAsync(Path.Combine(webDir, "GrpcServices.cs"),
                """
                namespace Acme.Grpc;

                public sealed class OrderRequest
                {
                }

                public sealed class OrderReply
                {
                }

                public sealed class OrderGrpcService
                {
                    public Task<OrderReply> GetOrder(OrderRequest request, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new OrderReply());
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new GrpcFixture(root);
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
