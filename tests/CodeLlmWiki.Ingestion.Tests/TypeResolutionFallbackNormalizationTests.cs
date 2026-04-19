using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class TypeResolutionFallbackNormalizationTests
{
    [Fact]
    public async Task Analyze_DoesNotEmitFallbackDiagnostics_ForNullableAndArrayPrimitiveTypes()
    {
        var fixture = await TypeNormalizationFixture.CreateAsync(
            """
            namespace Acme.Types;

            public sealed class Sample
            {
                public int? Count { get; set; }
                public string[] Names { get; set; } = [];
            }
            """);

        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        Assert.DoesNotContain(analysis.Diagnostics, x =>
            x.Code == "type:resolution:fallback"
            && x.Message.Contains("int?", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Diagnostics, x =>
            x.Code == "type:resolution:fallback"
            && x.Message.Contains("string[]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Analyze_DeduplicatesFallbackDiagnostics_ForRepeatedTupleMemberTypes()
    {
        var fixture = await TypeNormalizationFixture.CreateAsync(
            """
            namespace Acme.Types;

            public sealed class Sample
            {
                public (int a, int b) PairA { get; set; }
                public (int a, int b) PairB { get; set; }
            }
            """);

        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var tupleFallbackDiagnostics = analysis.Diagnostics
            .Where(x =>
                x.Code == "type:resolution:fallback"
                && x.Message.Contains("declared member type", StringComparison.Ordinal)
                && x.Message.Contains("(int a, int b)", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(tupleFallbackDiagnostics);
    }

    private sealed class TypeNormalizationFixture
    {
        private TypeNormalizationFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<TypeNormalizationFixture> CreateAsync(string source)
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-type-normalization-{Guid.NewGuid():N}", "fixture-repo");
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
                    <AssemblyName>Acme.TypeNormalization.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Sample.cs"), source);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new TypeNormalizationFixture(root);
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
