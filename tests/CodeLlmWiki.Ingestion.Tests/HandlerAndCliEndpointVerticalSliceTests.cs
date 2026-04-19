using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class HandlerAndCliEndpointVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesMessageHandlerAndCliEndpoints_WithCustomCatalogRule()
    {
        var fixture = await HandlerAndCliFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator(), BuildRuleCatalogWithCustomHandlerInterface());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var messageHandlerEndpoint = Assert.Single(model.Endpoints.Endpoints, x => x.Family == "message-handler");
        Assert.Equal("interface-handler", messageHandlerEndpoint.Kind);
        Assert.Equal("HANDLE", messageHandlerEndpoint.HttpMethod);
        Assert.Contains("message-handlers/iexecutecommand", messageHandlerEndpoint.NormalizedRouteKey, StringComparison.Ordinal);
        Assert.Equal("dotnet.message-handler.custom-interface-pattern", messageHandlerEndpoint.RuleId);
        Assert.Equal(EndpointConfidence.High, messageHandlerEndpoint.Confidence);
        Assert.NotNull(messageHandlerEndpoint.DeclaringTypeId);
        Assert.NotNull(messageHandlerEndpoint.DeclaringMethodId);

        var cliEndpoints = model.Endpoints.Endpoints.Where(x => x.Family == "cli").ToArray();
        var cliEndpoint = Assert.Single(cliEndpoints);
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
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator(), BuildRuleCatalogWithCustomHandlerInterface());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var handlerTypePage = pages.Single(x => x.RelativePath == "types/Acme/Handlers/CreateOrderHandler.md");
        Assert.Contains("## Endpoints", handlerTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[endpoints/message-handler/", handlerTypePage.Markdown, StringComparison.Ordinal);

        var handleMethodPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Handlers/CreateOrderHandler/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_name: Execute", StringComparison.Ordinal));
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

    [Fact]
    public async Task Query_AndRender_AreDeterministic_ForHandlerAndCliEndpoints()
    {
        var fixture = await HandlerAndCliFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator(), BuildRuleCatalogWithCustomHandlerInterface());

        var firstAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var secondAnalysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var firstModel = new ProjectStructureQueryService(firstAnalysis.Triples).GetModel(firstAnalysis.RepositoryId);
        var secondModel = new ProjectStructureQueryService(secondAnalysis.Triples).GetModel(secondAnalysis.RepositoryId);

        Assert.Equal(
            firstModel.Endpoints.Endpoints
                .Where(x => x.Family is "message-handler" or "cli")
                .Select(x => x.Id.Value)
                .ToArray(),
            secondModel.Endpoints.Endpoints
                .Where(x => x.Family is "message-handler" or "cli")
                .Select(x => x.Id.Value)
                .ToArray());

        Assert.Equal(
            firstModel.Endpoints.Groups
                .Where(x => x.Family is "message-handler" or "cli")
                .Select(x => x.Id.Value)
                .ToArray(),
            secondModel.Endpoints.Groups
                .Where(x => x.Family is "message-handler" or "cli")
                .Select(x => x.Id.Value)
                .ToArray());

        var firstPages = new ProjectStructureWikiRenderer().Render(firstModel)
            .Where(x => x.RelativePath.StartsWith("endpoints/message-handler/", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("endpoints/cli/", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("types/Acme/Handlers/CreateOrderHandler", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("types/Acme/Cli/SyncOptions", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("methods/Acme/Handlers/CreateOrderHandler/", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("methods/Acme/Cli/SyncOptions/", StringComparison.Ordinal))
            .Select(x => (x.RelativePath, x.Markdown))
            .ToArray();
        var secondPages = new ProjectStructureWikiRenderer().Render(secondModel)
            .Where(x => x.RelativePath.StartsWith("endpoints/message-handler/", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("endpoints/cli/", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("types/Acme/Handlers/CreateOrderHandler", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("types/Acme/Cli/SyncOptions", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("methods/Acme/Handlers/CreateOrderHandler/", StringComparison.Ordinal)
                        || x.RelativePath.StartsWith("methods/Acme/Cli/SyncOptions/", StringComparison.Ordinal))
            .Select(x => (x.RelativePath, x.Markdown))
            .ToArray();

        Assert.Equal(firstPages, secondPages);
    }

    private static EndpointRuleCatalog BuildRuleCatalogWithCustomHandlerInterface()
    {
        var handlerRules = EndpointRuleCatalog.Default.MessageHandlerInterfaceRules
            .Concat(
            [
                new HandlerInterfaceRule(
                    RuleId: "dotnet.message-handler.custom-interface-pattern",
                    RuleVersion: "1",
                    RuleSource: "catalog:test",
                    MatchKind: HandlerInterfaceMatchKind.ExactName,
                    MatchName: "IExecuteCommand"),
            ])
            .ToArray();

        return new EndpointRuleCatalog(
            catalogVersion: "test-1",
            messageHandlerInterfaceRules: handlerRules,
            cliVerbAttributeNamespace: EndpointRuleCatalog.Default.CliVerbAttributeNamespace,
            cliVerbAttributeTypeName: EndpointRuleCatalog.Default.CliVerbAttributeTypeName,
            cliVerbAttributeAssemblyName: EndpointRuleCatalog.Default.CliVerbAttributeAssemblyName);
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

                public interface IExecuteCommand<TCommand>
                {
                    void Execute(TCommand command);
                }

                public sealed class CreateOrderHandler : IExecuteCommand<CreateOrderCommand>
                {
                    public void Execute(CreateOrderCommand command)
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "FakeVerbAttribute.cs"),
                """
                namespace Acme.Attributes;

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class VerbAttribute : System.Attribute
                {
                    public VerbAttribute(string value)
                    {
                    }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "ShadowCli.cs"),
                """
                using Acme.Attributes;

                namespace Acme.Cli;

                [Verb("shadow")]
                public sealed class ShadowOptions
                {
                    public int Execute()
                    {
                        return 1;
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
