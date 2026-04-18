using System.Diagnostics;
using System.Text.Json;
using CodeLlmWiki.Cli.Features.Ingest;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ingestion.ProjectStructure;

namespace CodeLlmWiki.Cli.Tests;

public sealed class IngestionArtifactPublisherTests
{
    [Fact]
    public async Task PublishAsync_EmitsDeterministicWikiAndGraphMl_RunManifest_AndPromotesLatestOnSuccess()
    {
        var fixture = await PublisherFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var runResult = new IngestionRunResult(
            Status: IngestionRunStatus.SucceededWithDiagnostics,
            ExitCode: 0,
            Diagnostics: analysis.Diagnostics,
            RepositoryId: analysis.RepositoryId,
            Triples: analysis.Triples);

        var outputRoot = Path.Combine(Path.GetTempPath(), $"codellmwiki-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(outputRoot, "latest"));
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "latest", "stale.txt"), "stale");

        var publisher = new IngestionArtifactPublisher();

        var first = await publisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: fixture.RepositoryPath,
                OutputRootPath: outputRoot,
                StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero),
                RunResult: runResult),
            CancellationToken.None);

        var second = await publisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: fixture.RepositoryPath,
                OutputRootPath: outputRoot,
                StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 2, 0, TimeSpan.Zero),
                CompletedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 3, 0, TimeSpan.Zero),
                RunResult: runResult),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(first.LatestPromoted);
        Assert.True(second.Succeeded);
        Assert.True(second.LatestPromoted);
        Assert.False(File.Exists(Path.Combine(outputRoot, "latest", "stale.txt")));

        Assert.Contains(Directory.GetFiles(Path.Combine(first.RunDirectory, "wiki", "repositories"), "*.md"), _ => true);
        Assert.Contains(Directory.GetFiles(Path.Combine(first.RunDirectory, "wiki", "solutions"), "*.md"), _ => true);
        Assert.Contains(Directory.GetFiles(Path.Combine(first.RunDirectory, "wiki", "projects"), "*.md"), _ => true);
        Assert.Contains(Directory.GetFiles(Path.Combine(first.RunDirectory, "wiki", "packages"), "*.md"), _ => true);
        Assert.Contains(Directory.GetFiles(Path.Combine(first.RunDirectory, "wiki", "files"), "*.md"), _ => true);

        var graphMl = await File.ReadAllTextAsync(Path.Combine(first.RunDirectory, "graph", "graph.graphml"));
        Assert.Contains("<graphml", graphMl, StringComparison.Ordinal);
        var edgeCount = CountOccurrences(graphMl, "<edge ");
        Assert.Equal(runResult.Triples.Count, edgeCount);

        var firstWikiSnapshot = ReadDirectorySnapshot(Path.Combine(first.RunDirectory, "wiki"));
        var secondWikiSnapshot = ReadDirectorySnapshot(Path.Combine(second.RunDirectory, "wiki"));
        Assert.Equal(firstWikiSnapshot, secondWikiSnapshot);

        var firstGraph = await File.ReadAllTextAsync(Path.Combine(first.RunDirectory, "graph", "graph.graphml"));
        var secondGraph = await File.ReadAllTextAsync(Path.Combine(second.RunDirectory, "graph", "graph.graphml"));
        Assert.Equal(firstGraph, secondGraph);

        var latestManifestPath = Path.Combine(outputRoot, "latest", "manifest.json");
        var latestManifest = JsonDocument.Parse(await File.ReadAllTextAsync(latestManifestPath));
        Assert.Equal(second.RunId, latestManifest.RootElement.GetProperty("RunId").GetString());
        Assert.True(latestManifest.RootElement.GetProperty("LatestPromoted").GetBoolean());
        Assert.Equal(runResult.Triples.Count, latestManifest.RootElement.GetProperty("TripleCount").GetInt32());
        Assert.True(latestManifest.RootElement.GetProperty("DurationMs").GetInt64() > 0);
        Assert.True(latestManifest.RootElement.GetProperty("DiagnosticsSummary").GetArrayLength() >= 0);
        Assert.Equal("wiki", latestManifest.RootElement.GetProperty("Artifacts").GetProperty("Wiki").GetString());
        Assert.Equal("graph/graph.graphml", latestManifest.RootElement.GetProperty("Artifacts").GetProperty("GraphMl").GetString());
    }

    [Fact]
    public async Task PublishAsync_DoesNotPromoteLatest_WhenRunFailed()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"codellmwiki-artifacts-failed-{Guid.NewGuid():N}");
        var latestDirectory = Path.Combine(outputRoot, "latest");
        Directory.CreateDirectory(latestDirectory);
        var markerPath = Path.Combine(latestDirectory, "marker.txt");
        await File.WriteAllTextAsync(markerPath, "preserve");

        var runResult = new IngestionRunResult(
            Status: IngestionRunStatus.Failed,
            ExitCode: 1,
            Diagnostics: [new IngestionDiagnostic("ontology:invalid", "invalid")],
            RepositoryId: default,
            Triples: []);

        var publisher = new IngestionArtifactPublisher();
        var result = await publisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: ".",
                OutputRootPath: outputRoot,
                StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero),
                RunResult: runResult),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.LatestPromoted);
        Assert.True(File.Exists(markerPath));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "graph", "graph.graphml")));
    }

    [Fact]
    public async Task PublishAsync_DoesNotFail_WhenMethodSlugWouldExceedFileNameLimits()
    {
        var fixture = await PublisherFixture.CreateAsync(includeLongMethodSignature: true);
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var runResult = new IngestionRunResult(
            Status: IngestionRunStatus.SucceededWithDiagnostics,
            ExitCode: 0,
            Diagnostics: analysis.Diagnostics,
            RepositoryId: analysis.RepositoryId,
            Triples: analysis.Triples);

        var outputRoot = Path.Combine(Path.GetTempPath(), $"codellmwiki-artifacts-long-method-{Guid.NewGuid():N}");
        var publisher = new IngestionArtifactPublisher();

        var result = await publisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: fixture.RepositoryPath,
                OutputRootPath: outputRoot,
                StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero),
                RunResult: runResult),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.LatestPromoted);
        Assert.True(Directory.Exists(Path.Combine(result.RunDirectory, "wiki", "methods")));
        Assert.True(Directory.Exists(Path.Combine(result.RunDirectory, "wiki", "types")));
        Assert.True(Directory.Exists(Path.Combine(result.RunDirectory, "wiki", "namespaces")));
        Assert.True(Directory.Exists(Path.Combine(outputRoot, "latest")));

        var longestMethodFileNameLength = Directory
            .EnumerateFiles(Path.Combine(result.RunDirectory, "wiki", "methods"), "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path).Length)
            .DefaultIfEmpty(0)
            .Max();
        Assert.True(longestMethodFileNameLength <= 123, $"Unexpectedly long method wiki filename length: {longestMethodFileNameLength}");
    }

    [Fact]
    public async Task PublishAsync_Fails_AndDoesNotPromoteLatest_WhenScopedLinkInvariantsFail()
    {
        var fixture = await PublisherFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var runResult = new IngestionRunResult(
            Status: IngestionRunStatus.SucceededWithDiagnostics,
            ExitCode: 0,
            Diagnostics: analysis.Diagnostics,
            RepositoryId: analysis.RepositoryId,
            Triples: analysis.Triples);

        var outputRoot = Path.Combine(Path.GetTempPath(), $"codellmwiki-artifacts-invariant-fail-{Guid.NewGuid():N}");
        var latestDirectory = Path.Combine(outputRoot, "latest");
        Directory.CreateDirectory(latestDirectory);
        var markerPath = Path.Combine(latestDirectory, "marker.txt");
        await File.WriteAllTextAsync(markerPath, "preserve");

        var publisher = new IngestionArtifactPublisher(new FailingInvariantValidator());

        var result = await publisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: fixture.RepositoryPath,
                OutputRootPath: outputRoot,
                StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero),
                RunResult: runResult),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.LatestPromoted);
        Assert.True(File.Exists(markerPath));
        Assert.Contains("Scoped wiki link invariants failed", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("namespaces/Sample/App.md", result.FailureReason, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;

        while (index >= 0)
        {
            index = value.IndexOf(token, index, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            count++;
            index += token.Length;
        }

        return count;
    }

    private static IReadOnlyList<string> ReadDirectorySnapshot(string root)
    {
        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var content = File.ReadAllText(path);
                return $"{relative}\n{content}";
            })
            .ToArray();
    }

    private sealed class FailingInvariantValidator : IWikiScopedLinkInvariantValidator
    {
        public WikiScopedLinkInvariantValidationResult Validate(WikiScopedLinkInvariantValidationRequest request)
        {
            return new WikiScopedLinkInvariantValidationResult(
                [
                    new WikiScopedLinkInvariantViolation(
                        PageRelativePath: "namespaces/Sample/App.md",
                        SectionPath: "## Contained Types",
                        LineNumber: 12,
                        LineText: "- Worker (class)",
                        Message: "Expected wiki link bullet for resolvable target.")
                ]);
        }
    }

    private sealed class PublisherFixture
    {
        private PublisherFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<PublisherFixture> CreateAsync(bool includeLongMethodSignature = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-publisher-{Guid.NewGuid():N}", "fixture-repo");
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
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);

            var objDir = Path.Combine(appDir, "obj");
            Directory.CreateDirectory(objDir);
            await File.WriteAllTextAsync(Path.Combine(objDir, "project.assets.json"),
                """
                {
                  "libraries": {
                    "Newtonsoft.Json/13.0.3": {
                      "type": "package"
                    }
                  }
                }
                """);

            var programSource = includeLongMethodSignature
                ? """
                  using System.Collections.Generic;

                  namespace Sample.App;

                  public sealed class PathLengthProbe
                  {
                      public void VeryLongMethod(
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> first,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> second,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> third,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> fourth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> fifth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> sixth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> seventh,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> eighth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ninth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> tenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> eleventh,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> twelfth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> thirteenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> fourteenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> fifteenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> sixteenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> seventeenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> eighteenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> nineteenth,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> twentieth)
                      {
                      }
                  }
                  """
                : "Console.WriteLine(\"artifact\");\n";
            await File.WriteAllTextAsync(Path.Combine(appDir, "Program.cs"), programSource);
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# fixture\n");

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new PublisherFixture(root);
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
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stdOut}\n{stdErr}");
            }
        }
    }
}
