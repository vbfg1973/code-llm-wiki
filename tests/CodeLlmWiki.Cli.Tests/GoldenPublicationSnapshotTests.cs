using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeLlmWiki.Cli.Features.Ingest;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ingestion.ProjectStructure;

namespace CodeLlmWiki.Cli.Tests;

public sealed class GoldenPublicationSnapshotTests
{
    private static readonly Regex SnakeCaseKeyPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UtcIso8601Pattern = new(
        "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d{1,7})?Z$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public async Task PublicationSnapshot_MatchesGolden_ForWikiGraphMlAndManifest()
    {
        var fixture = await GoldenFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var runResult = new IngestionRunResult(
            Status: IngestionRunStatus.SucceededWithDiagnostics,
            ExitCode: 0,
            Diagnostics: analysis.Diagnostics,
            RepositoryId: analysis.RepositoryId,
            Triples: analysis.Triples);

        var outputRoot = fixture.OutputRoot;
        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        var publisher = new IngestionArtifactPublisher();
        var publishResult = await publisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: fixture.RepositoryPath,
                OutputRootPath: outputRoot,
                StartedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero),
                RunResult: runResult),
            CancellationToken.None);

        Assert.True(publishResult.Succeeded);

        var wikiRoot = Path.Combine(publishResult.RunDirectory, "wiki");
        ValidateFrontMatter(wikiRoot);

        var snapshot = BuildSnapshot(
            wikiRoot,
            Path.Combine(publishResult.RunDirectory, "graph", "graph.graphml"),
            publishResult.ManifestPath);

        var goldenPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "Golden", "publication-snapshot.json"));

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(goldenPath, json + Environment.NewLine);
            return;
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file: {goldenPath}");

        var expectedJson = await File.ReadAllTextAsync(goldenPath);
        var expected = JsonSerializer.Deserialize<PublicationSnapshot>(expectedJson);

        Assert.NotNull(expected);
        Assert.Equal(expected!.GraphMlHash, snapshot.GraphMlHash);
        Assert.Equal(expected.ManifestHash, snapshot.ManifestHash);
        Assert.Equal(expected.WikiFileHashes.Count, snapshot.WikiFileHashes.Count);

        foreach (var pair in expected.WikiFileHashes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Assert.True(snapshot.WikiFileHashes.TryGetValue(pair.Key, out var actualHash), $"Missing wiki file hash for {pair.Key}");
            Assert.Equal(pair.Value, actualHash);
        }
    }

    [Fact]
    public void FrontMatterValidation_Fails_OnInvalidEntityType()
    {
        var markdown =
            """
            ---
            entity_id: abc
            entity_type: Project
            repository_id: repo-1
            project_name: App
            project_path: src/App/App.csproj
            target_frameworks: [net10.0]
            discovery_method: msbuild
            ---

            # Project: App
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("projects/app.md", markdown));
    }

    [Fact]
    public void FrontMatterValidation_Fails_OnNonUtcTimestamp()
    {
        var markdown =
            """
            ---
            entity_id: file-1
            entity_type: file
            repository_id: repo-1
            file_name: Program.cs
            file_path: src/App/Program.cs
            ---

            # File: Program.cs

            ## Merge To Mainline
            - merge_commit: `abc123`
              merged_at_utc: `2026-01-01T00:00:00+01:00`
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("files/src/App/Program.cs.md", markdown));
    }

    private static PublicationSnapshot BuildSnapshot(string wikiRoot, string graphMlPath, string manifestPath)
    {
        var wikiHashes = Directory
            .EnumerateFiles(wikiRoot, "*.md", SearchOption.AllDirectories)
            .Select(path => new
            {
                RelativePath = Path.GetRelativePath(wikiRoot, path).Replace('\\', '/'),
                Hash = ComputeSha256(File.ReadAllText(path)),
            })
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToDictionary(x => x.RelativePath, x => x.Hash, StringComparer.Ordinal);

        return new PublicationSnapshot(
            WikiFileHashes: wikiHashes,
            GraphMlHash: ComputeSha256(File.ReadAllText(graphMlPath)),
            ManifestHash: ComputeSha256(File.ReadAllText(manifestPath)));
    }

    private static void ValidateFrontMatter(string wikiRoot)
    {
        foreach (var path in Directory.EnumerateFiles(wikiRoot, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(wikiRoot, path).Replace('\\', '/');
            var content = File.ReadAllText(path);
            ValidateFrontMatterDocument(relative, content);
        }
    }

    private static void ValidateFrontMatterDocument(string relativePath, string content)
    {
        Assert.StartsWith("---", content, StringComparison.Ordinal);

        var lines = content.Split('\n');
        var frontMatterEnd = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                frontMatterEnd = i;
                break;
            }
        }

        Assert.True(frontMatterEnd > 1, $"Invalid front matter in {relativePath}");
        var frontMatter = ParseFrontMatter(lines, frontMatterEnd, relativePath);

        ValidateRequiredFields(frontMatter, "common", "entity_id", "entity_type", "repository_id");
        Assert.True(
            frontMatter["entity_type"] is "repository" or "solution" or "project" or "package" or "namespace" or "type" or "file" or "index",
            $"Invalid entity_type '{frontMatter["entity_type"]}' in {relativePath}");

        if (relativePath.StartsWith("repositories/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "repository", "repository_name", "repository_path", "head_branch", "mainline_branch");
        }
        else if (relativePath.StartsWith("solutions/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "solution", "solution_name", "solution_path");
        }
        else if (relativePath.StartsWith("projects/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "project", "project_name", "project_path", "target_frameworks", "discovery_method");
        }
        else if (relativePath.StartsWith("packages/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "package", "package_id", "package_key");
        }
        else if (relativePath.StartsWith("files/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "file", "file_name", "file_path");
        }
        else if (relativePath.StartsWith("namespaces/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "namespace", "namespace_name", "namespace_path");
        }
        else if (relativePath.StartsWith("types/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "type", "type_name", "type_kind", "type_path", "accessibility");
        }

        ValidateTimestampLines(lines, relativePath);
    }

    private static Dictionary<string, string> ParseFrontMatter(string[] lines, int frontMatterEnd, string relativePath)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < frontMatterEnd; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            Assert.True(separatorIndex > 0, $"Invalid front matter line in {relativePath}: '{line}'");

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            Assert.Matches(SnakeCaseKeyPattern, key);
            Assert.False(values.ContainsKey(key), $"Duplicate front matter key '{key}' in {relativePath}");

            values[key] = value;
        }

        return values;
    }

    private static void ValidateRequiredFields(IReadOnlyDictionary<string, string> frontMatter, string groupName, params string[] keys)
    {
        foreach (var key in keys)
        {
            Assert.True(frontMatter.TryGetValue(key, out var value), $"Missing required {groupName} key '{key}'");
            Assert.False(string.IsNullOrWhiteSpace(value), $"Required {groupName} key '{key}' must be non-empty");
        }
    }

    private static void ValidateTimestampLines(IEnumerable<string> lines, string relativePath)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("_at_utc:", StringComparison.Ordinal))
            {
                continue;
            }

            var firstBacktick = trimmed.IndexOf('`');
            var lastBacktick = trimmed.LastIndexOf('`');
            Assert.True(firstBacktick >= 0 && lastBacktick > firstBacktick, $"Missing timestamp value delimiters in {relativePath}: '{trimmed}'");

            var timestamp = trimmed[(firstBacktick + 1)..lastBacktick];
            Assert.Matches(UtcIso8601Pattern, timestamp);
        }
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed record PublicationSnapshot(
        IReadOnlyDictionary<string, string> WikiFileHashes,
        string GraphMlHash,
        string ManifestHash);

    private sealed class GoldenFixture
    {
        private GoldenFixture(string repositoryPath, string outputRoot)
        {
            RepositoryPath = repositoryPath;
            OutputRoot = outputRoot;
        }

        public string RepositoryPath { get; }

        public string OutputRoot { get; }

        public static async Task<GoldenFixture> CreateAsync()
        {
            var baseRoot = Path.Combine(Path.GetTempPath(), "codellmwiki-golden-fixture");
            var repositoryPath = Path.Combine(baseRoot, "repo");
            var outputRoot = Path.Combine(baseRoot, "artifacts");

            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, recursive: true);
            }

            Directory.CreateDirectory(repositoryPath);

            var commitTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            string Commit(string message)
            {
                var timestamp = commitTimestamp.ToString("O");
                commitTimestamp = commitTimestamp.AddMinutes(1);

                RunGit(
                    repositoryPath,
                    new Dictionary<string, string>
                    {
                        ["GIT_AUTHOR_DATE"] = timestamp,
                        ["GIT_COMMITTER_DATE"] = timestamp,
                    },
                    "commit",
                    "-m",
                    message);

                return RunGit(repositoryPath, "rev-parse", "HEAD");
            }

            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(repositoryPath, "src", "App");
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "Program.cs"), "Console.WriteLine(\"golden\");\n");
            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "# golden\n");

            RunGit(repositoryPath, "init", "-b", "main");
            RunGit(repositoryPath, "config", "user.name", "Golden User");
            RunGit(repositoryPath, "config", "user.email", "golden@example.com");
            RunGit(repositoryPath, "add", ".");
            Commit("initial");

            return new GoldenFixture(repositoryPath, outputRoot);
        }

        private static string RunGit(string workingDirectory, params string[] args)
        {
            return RunGit(workingDirectory, null, args);
        }

        private static string RunGit(
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environmentVariables,
            params string[] args)
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

            if (environmentVariables is not null)
            {
                foreach (var pair in environmentVariables)
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }

            using var process = Process.Start(startInfo)!;
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stdOut}\n{stdErr}");
            }

            return stdOut.Trim();
        }
    }
}
