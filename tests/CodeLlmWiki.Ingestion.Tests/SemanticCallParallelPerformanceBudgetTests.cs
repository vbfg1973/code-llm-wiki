using System.Diagnostics;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class SemanticCallParallelPerformanceBudgetTests
{
    [Fact]
    public async Task AnalyzeAsync_ParallelSemanticCallProcessing_IsDeterministic_AndWithinBudget()
    {
        var fixture = await ParallelSemanticFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var serialWatch = Stopwatch.StartNew();
        var serial = await analyzer.AnalyzeAsync(
            fixture.RepositoryPath,
            CancellationToken.None,
            new ProjectStructureAnalysisOptions(SemanticCallGraphMaxDegreeOfParallelism: 1));
        serialWatch.Stop();

        var parallelWatch = Stopwatch.StartNew();
        var parallel = await analyzer.AnalyzeAsync(
            fixture.RepositoryPath,
            CancellationToken.None,
            new ProjectStructureAnalysisOptions(SemanticCallGraphMaxDegreeOfParallelism: 4));
        parallelWatch.Stop();

        Assert.NotEmpty(parallel.Triples);
        Assert.Equal(BuildTripleSnapshot(serial.Triples), BuildTripleSnapshot(parallel.Triples));
        Assert.Equal(BuildDiagnosticSnapshot(serial.Diagnostics), BuildDiagnosticSnapshot(parallel.Diagnostics));

        Assert.True(
            parallelWatch.ElapsedMilliseconds <= (serialWatch.ElapsedMilliseconds * 3) + 1_500,
            $"Parallel semantic processing overhead exceeded tolerance. serial={serialWatch.ElapsedMilliseconds}ms parallel={parallelWatch.ElapsedMilliseconds}ms");
    }

    private static IReadOnlyList<string> BuildTripleSnapshot(IReadOnlyList<SemanticTriple> triples)
    {
        return triples
            .Select(triple => $"{triple.Subject}|{triple.Predicate}|{triple.Object}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildDiagnosticSnapshot(IReadOnlyList<IngestionDiagnostic> diagnostics)
    {
        return diagnostics
            .Select(diagnostic => $"{diagnostic.Code}|{diagnostic.Message}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class ParallelSemanticFixture
    {
        private ParallelSemanticFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<ParallelSemanticFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-semantic-parallel-{Guid.NewGuid():N}", "fixture-repo");
            Directory.CreateDirectory(root);

            const int projectCount = 8;
            const int workerCountPerProject = 8;

            var slnx = new System.Text.StringBuilder();
            slnx.AppendLine("<Solution>");
            for (var i = 1; i <= projectCount; i++)
            {
                slnx.AppendLine($"  <Project Path=\"src/Project{i}/Project{i}.csproj\" />");
            }

            slnx.AppendLine("</Solution>");
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"), slnx.ToString());

            for (var i = 1; i <= projectCount; i++)
            {
                var projectDir = Path.Combine(root, "src", $"Project{i}");
                Directory.CreateDirectory(projectDir);

                await File.WriteAllTextAsync(Path.Combine(projectDir, $"Project{i}.csproj"),
                    $"""
                    <Project Sdk=\"Microsoft.NET.Sdk\">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <AssemblyName>Perf.Project{i}</AssemblyName>
                      </PropertyGroup>
                    </Project>
                    """);

                for (var worker = 1; worker <= workerCountPerProject; worker++)
                {
                    await File.WriteAllTextAsync(Path.Combine(projectDir, $"Worker{worker}.cs"),
                        $@"namespace Perf.Project{i};

public class Worker{worker}
{{
    public void Ping()
    {{
        System.Console.WriteLine(""{i}:{worker}"");
    }}
}}");
                }

                var caller = new System.Text.StringBuilder();
                caller.AppendLine($"namespace Perf.Project{i};");
                caller.AppendLine();
                caller.AppendLine("public class Caller");
                caller.AppendLine("{");
                caller.AppendLine("    public void CallAll()");
                caller.AppendLine("    {");

                for (var worker = 1; worker <= workerCountPerProject; worker++)
                {
                    caller.AppendLine($"        new Worker{worker}().Ping();");
                }

                caller.AppendLine("    }");
                caller.AppendLine("}");
                await File.WriteAllTextAsync(Path.Combine(projectDir, "Caller.cs"), caller.ToString());
            }

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new ParallelSemanticFixture(root);
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
