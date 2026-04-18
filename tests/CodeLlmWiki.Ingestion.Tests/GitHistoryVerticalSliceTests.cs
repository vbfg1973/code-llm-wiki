using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Ontology;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class GitHistoryVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_IngestsRenameAwareFileHistoryAndSummaryFields()
    {
        var fixture = await GitHistoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        Assert.Equal("main", model.Repository.HeadBranch);
        Assert.Equal("main", model.Repository.MainlineBranch);

        var file = model.Files.Single(x => x.Path == "src/App/Renamed.cs");
        Assert.Equal(5, file.EditCount);
        Assert.Equal(fixture.PostMergeTouchCommit, file.LastChange?.CommitSha);
        Assert.Equal("Alice", file.LastChange?.AuthorName);

        var historyShas = file.History.Select(x => x.CommitSha).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(fixture.InitialCommit, historyShas);
        Assert.Contains(fixture.FeatureEditCommit, historyShas);
        Assert.Contains(fixture.FeatureRenameCommit, historyShas);
        Assert.Contains(fixture.MergeCommit, historyShas);
        Assert.Contains(fixture.PostMergeTouchCommit, historyShas);
    }

    [Fact]
    public async Task Render_FilePageIncludesMergeToMainlineHistory()
    {
        var fixture = await GitHistoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var file = model.Files.Single(x => x.Path == "src/App/Renamed.cs");
        var merge = Assert.Single(file.MergeToMainlineEvents);
        Assert.Equal(fixture.MergeCommit, merge.MergeCommitSha);
        Assert.Equal("main", merge.TargetBranch);
        Assert.Equal(2, merge.SourceBranchFileCommitCount);

        var renderer = new ProjectStructureWikiRenderer();
        var pages = renderer.Render(model);
        var page = pages.Single(x => x.RelativePath.StartsWith("files/", StringComparison.Ordinal)
            && x.Markdown.Contains("src/App/Renamed.cs", StringComparison.Ordinal));

        Assert.Contains("branch_snapshot: main", page.Markdown, StringComparison.Ordinal);
        Assert.Contains("source_branch_file_commit_count: 2", page.Markdown, StringComparison.Ordinal);
        Assert.Contains($"merge_commit: `{fixture.MergeCommit}`", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_RepresentsSubmodulePresenceAsOpaqueDependencies()
    {
        var fixture = await GitHistoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var submodule = Assert.Single(model.Submodules);
        Assert.Equal("deps/shared", submodule.Path);
        Assert.Equal("https://example.com/shared.git", submodule.Url);
        Assert.DoesNotContain(model.Projects, x => x.Path.StartsWith("deps/shared/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ontology_ContainsGitHistoryPredicates_AndStillValidates()
    {
        var ontologyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ontology", "ontology.v1.yaml"));
        var loader = new OntologyLoader();
        var result = await loader.LoadAsync(ontologyPath, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Definition);

        var predicateIds = result.Definition!.Predicates.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("core:headBranch", predicateIds);
        Assert.Contains("core:mainlineBranch", predicateIds);
        Assert.Contains("core:hasHistoryEvent", predicateIds);
        Assert.Contains("core:commitSha", predicateIds);
        Assert.Contains("core:committedAtUtc", predicateIds);
        Assert.Contains("core:authorName", predicateIds);
        Assert.Contains("core:authorEmail", predicateIds);
        Assert.Contains("core:editCount", predicateIds);
        Assert.Contains("core:lastChangeCommitSha", predicateIds);
        Assert.Contains("core:lastChangedAtUtc", predicateIds);
        Assert.Contains("core:lastChangedBy", predicateIds);
        Assert.Contains("core:isMergeToMainline", predicateIds);
        Assert.Contains("core:targetBranch", predicateIds);
        Assert.Contains("core:sourceBranchFileCommitCount", predicateIds);
        Assert.Contains("core:hasSubmodule", predicateIds);
        Assert.Contains("core:submoduleUrl", predicateIds);
    }

    private sealed class GitHistoryFixture
    {
        private GitHistoryFixture(
            string repositoryPath,
            string initialCommit,
            string featureEditCommit,
            string featureRenameCommit,
            string mergeCommit,
            string postMergeTouchCommit)
        {
            RepositoryPath = repositoryPath;
            InitialCommit = initialCommit;
            FeatureEditCommit = featureEditCommit;
            FeatureRenameCommit = featureRenameCommit;
            MergeCommit = mergeCommit;
            PostMergeTouchCommit = postMergeTouchCommit;
        }

        public string RepositoryPath { get; }

        public string InitialCommit { get; }

        public string FeatureEditCommit { get; }

        public string FeatureRenameCommit { get; }

        public string MergeCommit { get; }

        public string PostMergeTouchCommit { get; }

        public static async Task<GitHistoryFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-githistory-{Guid.NewGuid():N}", "history-repo");
            Directory.CreateDirectory(root);
            var commitTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            string Commit(string message)
            {
                var timestamp = commitTimestamp.ToString("O");
                commitTimestamp = commitTimestamp.AddMinutes(1);

                RunGit(
                    root,
                    new Dictionary<string, string>
                    {
                        ["GIT_AUTHOR_DATE"] = timestamp,
                        ["GIT_COMMITTER_DATE"] = timestamp,
                    },
                    "commit",
                    "-m",
                    message);

                return RunGit(root, "rev-parse", "HEAD");
            }

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
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Original.cs"), "public class Original { }\n");
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# history\n");
            await File.WriteAllTextAsync(Path.Combine(root, ".gitmodules"),
                """
                [submodule "deps/shared"]
                path = deps/shared
                url = https://example.com/shared.git
                """);

            var submoduleDir = Path.Combine(root, "deps", "shared");
            Directory.CreateDirectory(submoduleDir);
            await File.WriteAllTextAsync(Path.Combine(submoduleDir, "Injected.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            RunGit(root, "init", "-b", "main");
            ConfigureIdentity(root, "Alice", "alice@example.com");
            RunGit(root, "add", ".");
            var initialCommit = Commit("initial");

            RunGit(root, "checkout", "-b", "feature/rename");
            ConfigureIdentity(root, "Bob", "bob@example.com");

            await File.AppendAllTextAsync(Path.Combine(appDir, "Original.cs"), "public void StepOne() { }\n");
            RunGit(root, "add", ".");
            var featureEditCommit = Commit("feature edit");

            RunGit(root, "mv", "src/App/Original.cs", "src/App/Renamed.cs");
            await File.AppendAllTextAsync(Path.Combine(appDir, "Renamed.cs"), "public void StepTwo() { }\n");
            RunGit(root, "add", ".");
            var featureRenameCommit = Commit("rename file");

            RunGit(root, "checkout", "main");
            ConfigureIdentity(root, "Alice", "alice@example.com");

            await File.AppendAllTextAsync(Path.Combine(root, "README.md"), "mainline update\n");
            RunGit(root, "add", "README.md");
            Commit("mainline docs update");

            var mergeTimestamp = commitTimestamp.ToString("O");
            commitTimestamp = commitTimestamp.AddMinutes(1);
            RunGit(
                root,
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = mergeTimestamp,
                    ["GIT_COMMITTER_DATE"] = mergeTimestamp,
                },
                "merge",
                "--no-ff",
                "feature/rename",
                "-m",
                "merge feature rename");
            var mergeCommit = RunGit(root, "rev-parse", "HEAD");

            await File.AppendAllTextAsync(Path.Combine(appDir, "Renamed.cs"), "public void MainlineTouch() { }\n");
            RunGit(root, "add", "src/App/Renamed.cs");
            var postMergeTouchCommit = Commit("post merge touch");

            return new GitHistoryFixture(
                root,
                initialCommit,
                featureEditCommit,
                featureRenameCommit,
                mergeCommit,
                postMergeTouchCommit);
        }

        private static void ConfigureIdentity(string workingDirectory, string name, string email)
        {
            RunGit(workingDirectory, "config", "user.name", name);
            RunGit(workingDirectory, "config", "user.email", email);
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
