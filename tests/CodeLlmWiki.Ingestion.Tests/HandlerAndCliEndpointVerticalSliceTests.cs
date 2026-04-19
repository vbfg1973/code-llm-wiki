using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class HandlerAndCliEndpointVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesMessageHandlerAndCliEndpoints()
    {
        var fixture = await HandlerAndCliFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var messageHandlerEndpoint = Assert.Single(model.Endpoints.Endpoints, x => x.Family == "message-handler");
        Assert.Equal("interface-handler", messageHandlerEndpoint.Kind);
        Assert.Equal("HANDLE", messageHandlerEndpoint.HttpMethod);
        Assert.Contains("message-handlers/", messageHandlerEndpoint.NormalizedRouteKey, StringComparison.Ordinal);
        Assert.Equal("dotnet.message-handler.interface-pattern", messageHandlerEndpoint.RuleId);
        Assert.Equal(EndpointConfidence.High, messageHandlerEndpoint.Confidence);
        Assert.NotNull(messageHandlerEndpoint.DeclaringTypeId);
        Assert.NotNull(messageHandlerEndpoint.DeclaringMethodId);

        var cliEndpoint = Assert.Single(model.Endpoints.Endpoints, x => x.Family == "cli");
        Assert.Equal("cli-command", cliEndpoint.Kind);
        Assert.Equal("COMMAND", cliEndpoint.HttpMethod);
        Assert.Equal("cli/sync", cliEndpoint.NormalizedRouteKey);
        Assert.Equal("cli.commandlineparser.verb-attribute", cliEndpoint.RuleId);
        Assert.NotNull(cliEndpoint.DeclaringTypeId);
        Assert.NotNull(cliEndpoint.DeclaringMethodId);
    }

    [Fact]
    public async Task Render_TypeAndMethodPages_LinkToDiscoveredHandlerAndCliEndpoints()
    {
        var fixture = await HandlerAndCliFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var handlerTypePage = pages.Single(x => x.RelativePath == "types/Acme/Handlers/CreateOrderHandler.md");
        Assert.Contains("## Endpoints", handlerTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[endpoints/message-handler/", handlerTypePage.Markdown, StringComparison.Ordinal);

        var handleMethodPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Handlers/CreateOrderHandler/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_name: Handle", StringComparison.Ordinal));
        Assert.Contains("## Endpoints", handleMethodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[endpoints/message-handler/", handleMethodPage.Markdown, StringComparison.Ordinal);

        var cliTypePage = pages.Single(x => x.RelativePath == "types/Acme/Cli/SyncOptions.md");
        Assert.Contains("## Endpoints", cliTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[endpoints/cli/", cliTypePage.Markdown, StringComparison.Ordinal);

        var executeMethodPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Cli/SyncOptions/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_name: Execute", StringComparison.Ordinal));
        Assert.Contains("## Endpoints", executeMethodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[endpoints/cli/", executeMethodPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class HandlerAndCliFixture
    {
        private HandlerAndCliFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<HandlerAndCliFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-handler-cli-{Guid.NewGuid():N}", "handler-cli-repo");
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
                    <AssemblyName>Acme.HandlerCli.App</AssemblyName>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="CommandLineParser" Version="2.9.1" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Handlers.cs"),
                """
                namespace Acme.Handlers;

                public sealed record CreateOrderCommand(string OrderId);

                public interface ICommandHandler<TCommand>
                {
                    void Handle(TCommand command);
                }

                public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
                {
                    public void Handle(CreateOrderCommand command)
                    {
                    }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Cli.cs"),
                """
                using CommandLine;

                namespace Acme.Cli;

                [Verb("sync")]
                public sealed class SyncOptions
                {
                    public int Execute()
                    {
                        return 0;
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new HandlerAndCliFixture(root);
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
