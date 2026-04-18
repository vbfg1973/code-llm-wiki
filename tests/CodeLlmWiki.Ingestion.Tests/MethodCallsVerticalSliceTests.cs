using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MethodCallsVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesInternalCalls_ExternalUsage_AndExtensionMetadata()
    {
        var fixture = await MethodCallsFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var callerType = model.Declarations.Types.Single(x => x.Name == "Caller");
        var calleeType = model.Declarations.Types.Single(x => x.Name == "Callee");
        var extensionType = model.Declarations.Types.Single(x => x.Name == "CalleeExtensions");

        var callAll = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == callerType.Id && x.Name == "CallAll");
        var invoke = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == callerType.Id && x.Name == "Invoke");
        var ping = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == calleeType.Id && x.Name == "Ping");
        var touch = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == extensionType.Id && x.Name == "Touch");

        Assert.True(touch.IsExtensionMethod);
        Assert.Equal(calleeType.Id, touch.ExtendedType?.TypeId);

        var outgoingCalls = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.Calls && x.SourceMethodId == callAll.Id)
            .ToArray();

        Assert.Contains(outgoingCalls, x => x.TargetMethodId == ping.Id);
        Assert.Contains(outgoingCalls, x => x.TargetMethodId == touch.Id);
        Assert.Contains(outgoingCalls, x => x.ExternalTargetType is not null && !string.IsNullOrWhiteSpace(x.ExternalAssemblyName));
        Assert.Contains(outgoingCalls, x => x.TargetMethodId is null && x.ExternalTargetType?.ResolutionStatus == DeclarationResolutionStatus.Unresolved);
        Assert.Contains(analysis.Diagnostics, x => x.Code == "method:call:resolution:failed");

        var incomingCalls = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.Calls && x.TargetMethodId == callAll.Id)
            .ToArray();

        Assert.Contains(incomingCalls, x => x.SourceMethodId == invoke.Id);
    }

    [Fact]
    public async Task Render_MethodPages_IncludeCallsAndCalledBySections()
    {
        var fixture = await MethodCallsFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var callAllPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Calls/Caller/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_signature: Acme.Calls.Caller.CallAll()", StringComparison.Ordinal));

        Assert.Contains("## Calls", callAllPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Called By", callAllPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Ping()", callAllPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Touch(", callAllPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("external", callAllPage.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unresolved", callAllPage.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke()", callAllPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class MethodCallsFixture
    {
        private MethodCallsFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MethodCallsFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-method-calls-{Guid.NewGuid():N}", "method-call-repo");
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
                    <AssemblyName>Acme.Calls.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Calls.cs"),
                """
                using System;

                namespace Acme.Calls;

                public class Callee
                {
                    public void Ping() { }
                }

                public static class CalleeExtensions
                {
                    public static void Touch(this Callee callee)
                    {
                        callee.Ping();
                    }
                }

                public class Caller
                {
                    private readonly Callee _callee = new();

                    public void CallAll()
                    {
                        _callee.Ping();
                        _callee.Touch();
                        Console.WriteLine("hello");
                        UnknownApi.Missing();
                    }

                    public void Invoke()
                    {
                        CallAll();
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MethodCallsFixture(root);
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
