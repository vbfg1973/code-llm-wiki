using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MetricPerformanceBudgetValidationTests
{
    [Fact]
    public async Task QueryPipeline_StaysWithinPerformanceBudget_OnRepresentativeFixture()
    {
        var fixture = await PerformanceFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var serialWatch = Stopwatch.StartNew();
        var serialModel = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                MetricComputationMaxDegreeOfParallelism = 1,
            });
        serialWatch.Stop();

        var parallelWatch = Stopwatch.StartNew();
        var parallelModel = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                MetricComputationMaxDegreeOfParallelism = 4,
            });
        parallelWatch.Stop();

        Assert.NotEmpty(serialModel.Hotspots.PrimaryRankings);
        Assert.NotEmpty(parallelModel.Hotspots.PrimaryRankings);

        Assert.True(serialWatch.ElapsedMilliseconds < 15_000, $"Serial query exceeded budget: {serialWatch.ElapsedMilliseconds}ms");
        Assert.True(parallelWatch.ElapsedMilliseconds < 15_000, $"Parallel query exceeded budget: {parallelWatch.ElapsedMilliseconds}ms");
        Assert.True(parallelWatch.ElapsedMilliseconds <= (serialWatch.ElapsedMilliseconds * 3) + 500,
            $"Parallel query overhead exceeded tolerance. serial={serialWatch.ElapsedMilliseconds}ms parallel={parallelWatch.ElapsedMilliseconds}ms");
    }

    private sealed class PerformanceFixture
    {
        private PerformanceFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<PerformanceFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-performance-{Guid.NewGuid():N}", "perf-repo");
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
                    <AssemblyName>Acme.Performance</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            var source = BuildSourceText();
            await File.WriteAllTextAsync(Path.Combine(appDir, "GeneratedWorkload.cs"), source);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new PerformanceFixture(root);
        }

        private static string BuildSourceText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("namespace Acme.Performance;");
            sb.AppendLine();
            sb.AppendLine("public class SharedDependency { }");
            sb.AppendLine();

            for (var i = 0; i < 60; i++)
            {
                sb.AppendLine($"public class Worker{i:000}");
                sb.AppendLine("{");
                sb.AppendLine("    private readonly SharedDependency _dependency = new();");
                sb.AppendLine();
                sb.AppendLine("    public int Execute(int input)");
                sb.AppendLine("    {");
                sb.AppendLine("        var value = input;");
                sb.AppendLine("        if (value > 10)");
                sb.AppendLine("        {");
                sb.AppendLine("            value += 2;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        for (var i = 0; i < 3; i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            value += i;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        return value;");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                sb.AppendLine();
            }

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
