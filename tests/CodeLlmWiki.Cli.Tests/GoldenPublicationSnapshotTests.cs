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
        var snapshot = await BuildPublicationSnapshotAsync(fixture);

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
    public async Task PublicationSnapshot_IsDeterministic_AcrossRepeatedRuns()
    {
        var fixture = await GoldenFixture.CreateAsync();
        var first = await BuildPublicationSnapshotAsync(fixture);
        var second = await BuildPublicationSnapshotAsync(fixture);

        Assert.Equal(first.GraphMlHash, second.GraphMlHash);
        Assert.Equal(first.ManifestHash, second.ManifestHash);
        Assert.Equal(first.WikiFileHashes, second.WikiFileHashes);
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

    [Fact]
    public void FrontMatterValidation_Fails_OnIncompleteNamespaceParentPair()
    {
        var markdown =
            """
            ---
            entity_id: namespace-1
            entity_type: namespace
            repository_id: repo-1
            namespace_name: Sample
            namespace_path: Sample
            parent_namespace_id: namespace-parent
            ---

            # Namespace: Sample
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("namespaces/Sample.md", markdown));
    }

    [Fact]
    public void FrontMatterValidation_Fails_OnIncompletePrimaryProjectContext()
    {
        var markdown =
            """
            ---
            entity_id: type-1
            entity_type: type
            repository_id: repo-1
            type_name: Thing
            type_kind: class
            type_path: Sample.Thing
            accessibility: public
            constructor_count: 0
            method_count: 1
            property_count: 0
            field_count: 0
            enum_member_count: 0
            record_parameter_count: 0
            behavioral_method_count: 1
            primary_project_id: project-1
            ---

            # Type: Thing
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("types/Sample/Thing.md", markdown));
    }

    [Fact]
    public void FrontMatterValidation_Fails_OnMissingTypeCountScalar()
    {
        var markdown =
            """
            ---
            entity_id: type-1
            entity_type: type
            repository_id: repo-1
            type_name: Thing
            type_kind: class
            type_path: Sample.Thing
            accessibility: public
            constructor_count: 0
            method_count: 1
            property_count: 0
            field_count: 0
            enum_member_count: 0
            behavioral_method_count: 1
            ---

            # Type: Thing
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("types/Sample/Thing.md", markdown));
    }

    [Fact]
    public void FrontMatterValidation_Fails_OnGuidanceMissingHeadBranch()
    {
        var markdown =
            """
            ---
            entity_id: guidance:llm:repo-1
            entity_type: guidance
            repository_id: repo-1
            guidance_kind: llm
            ---

            # LLM Contract
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("guidance/llm-contract.md", markdown));
    }

    [Fact]
    public void BodyValidation_Fails_OnGuidanceContractMissingRequiredSections()
    {
        var markdown =
            """
            ---
            entity_id: guidance:llm-contract:repo-1
            entity_type: guidance
            repository_id: repo-1
            guidance_kind: llm
            head_branch: develop
            ---

            # LLM Contract

            ## Start Here
            - [Repository Index](index/repository-index.md)
            """;

        Assert.ThrowsAny<Exception>(() => ValidateFrontMatterDocument("guidance/llm-contract.md", markdown));
    }

    private static async Task<PublicationSnapshot> BuildPublicationSnapshotAsync(GoldenFixture fixture)
    {
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
        ValidateEndpointAnchorLinkContracts(wikiRoot);

        return BuildSnapshot(
            wikiRoot,
            Path.Combine(publishResult.RunDirectory, "graph", "graph.graphml"),
            publishResult.ManifestPath);
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
            frontMatter["entity_type"] is "repository" or "solution" or "project" or "package" or "namespace" or "type" or "method" or "file" or "index" or "hotspot" or "guidance" or "endpoint" or "endpoint_group",
            $"Invalid entity_type '{frontMatter["entity_type"]}' in {relativePath}");

        if (relativePath.StartsWith("repositories/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "repository", "repository_name", "repository_path", "head_branch", "mainline_branch");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "repository_name", "repository_path", "head_branch", "mainline_branch");
        }
        else if (relativePath.StartsWith("solutions/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "solution", "solution_name", "solution_path");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "solution_name", "solution_path");
        }
        else if (relativePath.StartsWith("projects/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "project", "project_name", "project_path", "target_frameworks", "discovery_method");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "project_name", "project_path", "target_frameworks", "discovery_method");
        }
        else if (relativePath.StartsWith("packages/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "package", "package_id", "package_key");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "package_id", "package_key");
        }
        else if (relativePath.StartsWith("files/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "file", "file_name", "file_path");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "file_name", "file_path");
        }
        else if (relativePath.StartsWith("namespaces/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "namespace", "namespace_name", "namespace_path");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "namespace_name", "namespace_path", "parent_namespace_id", "parent_namespace_name");

            var hasParentId = frontMatter.ContainsKey("parent_namespace_id");
            var hasParentName = frontMatter.ContainsKey("parent_namespace_name");
            Assert.True(hasParentId == hasParentName, $"Namespace parent keys must be emitted as a complete pair in {relativePath}");
        }
        else if (relativePath.StartsWith("types/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(
                frontMatter,
                "type",
                "type_name",
                "type_kind",
                "type_path",
                "accessibility",
                "constructor_count",
                "method_count",
                "property_count",
                "field_count",
                "enum_member_count",
                "record_parameter_count",
                "behavioral_method_count");
            ValidateAllowedFields(
                frontMatter,
                "entity_id",
                "entity_type",
                "repository_id",
                "type_name",
                "type_kind",
                "type_path",
                "accessibility",
                "constructor_count",
                "method_count",
                "property_count",
                "field_count",
                "enum_member_count",
                "record_parameter_count",
                "behavioral_method_count",
                "is_nested_type",
                "is_partial_type",
                "namespace_name",
                "declaring_type_id",
                "declaring_type_name",
                "primary_project_id",
                "primary_project_name",
                "primary_assembly_name",
                "primary_project_path");

            var hasDeclaringTypeId = frontMatter.ContainsKey("declaring_type_id");
            var hasDeclaringTypeName = frontMatter.ContainsKey("declaring_type_name");
            Assert.True(hasDeclaringTypeId == hasDeclaringTypeName, $"Type declaring-type keys must be emitted as a complete pair in {relativePath}");

            var primaryProjectKeys = new[]
            {
                "primary_project_id",
                "primary_project_name",
                "primary_assembly_name",
                "primary_project_path",
            };
            var presentPrimaryKeyCount = primaryProjectKeys.Count(frontMatter.ContainsKey);
            Assert.True(
                presentPrimaryKeyCount is 0 or 4,
                $"Primary project/assembly keys must be emitted as a complete set in {relativePath}");

            ValidateNonNegativeIntegerField(frontMatter, "constructor_count", relativePath);
            ValidateNonNegativeIntegerField(frontMatter, "method_count", relativePath);
            ValidateNonNegativeIntegerField(frontMatter, "property_count", relativePath);
            ValidateNonNegativeIntegerField(frontMatter, "field_count", relativePath);
            ValidateNonNegativeIntegerField(frontMatter, "enum_member_count", relativePath);
            ValidateNonNegativeIntegerField(frontMatter, "record_parameter_count", relativePath);
            ValidateNonNegativeIntegerField(frontMatter, "behavioral_method_count", relativePath);
        }
        else if (relativePath.StartsWith("methods/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "method", "method_name", "method_kind", "method_signature", "accessibility", "declaring_type_id");
            ValidateAllowedFields(
                frontMatter,
                "entity_id",
                "entity_type",
                "repository_id",
                "method_name",
                "method_kind",
                "method_signature",
                "accessibility",
                "declaring_type_id",
                "declaring_type_name");

            var hasDeclaringTypeId = frontMatter.ContainsKey("declaring_type_id");
            var hasDeclaringTypeName = frontMatter.ContainsKey("declaring_type_name");
            Assert.True(hasDeclaringTypeId == hasDeclaringTypeName, $"Method declaring-type keys must be emitted as a complete pair in {relativePath}");
        }
        else if (relativePath.StartsWith("endpoints/", StringComparison.Ordinal))
        {
            if (relativePath.Contains("/groups/", StringComparison.Ordinal))
            {
                ValidateRequiredFields(frontMatter, "endpoint_group", "endpoint_family", "endpoint_group_key");
                ValidateAllowedFields(
                    frontMatter,
                    "entity_id",
                    "entity_type",
                    "repository_id",
                    "endpoint_family",
                    "endpoint_group_key");
            }
            else
            {
                ValidateRequiredFields(frontMatter, "endpoint", "endpoint_family", "endpoint_kind", "endpoint_http_method", "endpoint_route_key", "endpoint_fingerprint");
                ValidateAllowedFields(
                    frontMatter,
                    "entity_id",
                    "entity_type",
                    "repository_id",
                    "endpoint_family",
                    "endpoint_kind",
                    "endpoint_http_method",
                    "endpoint_route_key",
                    "endpoint_fingerprint",
                    "endpoint_resolution_reason");
            }
        }
        else if (relativePath.StartsWith("index/", StringComparison.Ordinal))
        {
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id");
        }
        else if (relativePath.StartsWith("hotspots/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "hotspot", "hotspot_kind");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "hotspot_kind");
        }
        else if (relativePath.StartsWith("guidance/", StringComparison.Ordinal))
        {
            ValidateRequiredFields(frontMatter, "guidance", "guidance_kind", "head_branch");
            ValidateAllowedFields(frontMatter, "entity_id", "entity_type", "repository_id", "guidance_kind", "head_branch");
        }

        ValidateTimestampLines(lines, relativePath);

        var body = string.Join('\n', lines[(frontMatterEnd + 1)..]).TrimStart();
        ValidateBodyReadability(relativePath, body, frontMatter);
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

    private static void ValidateAllowedFields(IReadOnlyDictionary<string, string> frontMatter, params string[] allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in frontMatter.Keys)
        {
            Assert.True(allowed.Contains(key), $"Unexpected front matter key '{key}'");
        }
    }

    private static void ValidateBodyReadability(
        string relativePath,
        string body,
        IReadOnlyDictionary<string, string> frontMatter)
    {
        if (relativePath.StartsWith("index/", StringComparison.Ordinal))
        {
            Assert.Contains("## Guidance", body, StringComparison.Ordinal);
            Assert.Contains("[Human Guide](guidance/human.md)", body, StringComparison.Ordinal);
            Assert.Contains("[LLM Contract](guidance/llm-contract.md)", body, StringComparison.Ordinal);
            Assert.Contains("## Endpoint Groups", body, StringComparison.Ordinal);
            Assert.Contains("## Endpoints", body, StringComparison.Ordinal);
            Assert.Contains("## Endpoint Diagnostics", body, StringComparison.Ordinal);
            return;
        }

        if (relativePath.StartsWith("repositories/", StringComparison.Ordinal))
        {
            Assert.Contains("## Guidance", body, StringComparison.Ordinal);
            Assert.Contains("[Human Guide](guidance/human.md)", body, StringComparison.Ordinal);
            Assert.Contains("[LLM Contract](guidance/llm-contract.md)", body, StringComparison.Ordinal);
        }

        if (relativePath == "guidance/human.md")
        {
            Assert.Contains("## Start Here", body, StringComparison.Ordinal);
        }

        if (relativePath == "guidance/llm-contract.md")
        {
            Assert.Contains("## Contract Rules", body, StringComparison.Ordinal);
            Assert.Contains("## Response Template", body, StringComparison.Ordinal);
            Assert.Contains("## Link Policy", body, StringComparison.Ordinal);
            Assert.Contains("## Evidence Policy", body, StringComparison.Ordinal);
            Assert.Contains("## Guardrails", body, StringComparison.Ordinal);
            Assert.Contains("## Named Recipes", body, StringComparison.Ordinal);
            Assert.Contains("## Capability Matrix", body, StringComparison.Ordinal);
            Assert.Contains("<a id=\"recipe-structure-survey\"></a>", body, StringComparison.Ordinal);
            Assert.Contains("<a id=\"recipe-hotspot-triage\"></a>", body, StringComparison.Ordinal);
            Assert.Contains("<a id=\"recipe-dependency-trace\"></a>", body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("entity_id", body, StringComparison.Ordinal);
        Assert.DoesNotContain("repository_id", body, StringComparison.Ordinal);
        Assert.DoesNotContain(frontMatter["entity_id"], body, StringComparison.Ordinal);
    }

    private static void ValidateEndpointAnchorLinkContracts(string wikiRoot)
    {
        var markdownByRelativePath = Directory
            .EnumerateFiles(wikiRoot, "*.md", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(wikiRoot, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);

        var endpointPackageLinkPattern = new Regex(@"\((?<path>packages\/[^)#\s]+\.md)#(?<anchor>[^)\s]+)\)", RegexOptions.CultureInvariant);

        foreach (var page in markdownByRelativePath.Where(x => x.Key.StartsWith("endpoints/", StringComparison.Ordinal)))
        {
            var matches = endpointPackageLinkPattern.Matches(page.Value);
            foreach (Match match in matches)
            {
                var packageRelativePath = match.Groups["path"].Value;
                var anchor = match.Groups["anchor"].Value;
                Assert.True(
                    markdownByRelativePath.TryGetValue(packageRelativePath, out var packageMarkdown),
                    $"Endpoint link target '{packageRelativePath}' missing for page '{page.Key}'.");
                Assert.Contains(
                    $"<a id=\"{anchor}\"></a>",
                    packageMarkdown!,
                    StringComparison.Ordinal);
            }
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

    private static void ValidateNonNegativeIntegerField(
        IReadOnlyDictionary<string, string> frontMatter,
        string key,
        string relativePath)
    {
        Assert.True(frontMatter.TryGetValue(key, out var value), $"Missing required scalar key '{key}' in {relativePath}");
        Assert.True(int.TryParse(value, out var parsed), $"Scalar key '{key}' must be an integer in {relativePath}");
        Assert.True(parsed >= 0, $"Scalar key '{key}' must be non-negative in {relativePath}");
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
                    <PackageReference Include="CommandLineParser" Version="2.9.1" />
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
                    },
                    "CommandLineParser/2.9.1": {
                      "type": "package"
                    }
                  }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Program.cs"),
                """
                namespace Golden.Sample;

                public interface IWorker
                {
                    void Run();
                }

                public sealed class Worker : IWorker
                {
                    private int _count;
                    public int Count { get; private set; }

                    public void Run()
                    {
                        _count++;
                        Count = _count;
                        MissingApi.Call();
                    }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(appDir, "EndpointController.cs"),
                """
                using Microsoft.AspNetCore.Mvc;

                namespace Golden.Sample.Endpoints;

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
                app.MapGet("/health", () => Results.Ok("ok"));
                app.Run();
                """);
            await File.WriteAllTextAsync(Path.Combine(appDir, "Handler.cs"),
                """
                namespace Golden.Sample.Messages;

                public sealed record Ping(string Value);

                public interface IMessageHandler<TCommand>
                {
                    void Handle(TCommand command);
                }

                public sealed class PingHandler : IMessageHandler<Ping>
                {
                    public void Handle(Ping command)
                    {
                    }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(appDir, "Cli.cs"),
                """
                using CommandLine;

                namespace Golden.Sample.Cli;

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
                namespace Golden.Sample.Grpc;

                public sealed class KnownGrpcService
                {
                    public void Handle()
                    {
                    }
                }

                public static class GrpcBoot
                {
                    public static void Register(WebApplication app)
                    {
                        app.MapGrpcService<KnownGrpcService>();
                        app.MapGrpcService<MissingGrpcService>();
                    }
                }
                """);
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
