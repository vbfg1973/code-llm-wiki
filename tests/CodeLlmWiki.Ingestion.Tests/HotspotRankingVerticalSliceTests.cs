using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class HotspotRankingVerticalSliceTests
{
    [Fact]
    public async Task Query_ProducesDeterministicPrimaryAndCompositeRankings()
    {
        var fixture = await HotspotRankingFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var options = new ProjectStructureQueryOptions
        {
            MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
        };

        var first = query.GetModel(analysis.RepositoryId, options);
        var second = query.GetModel(analysis.RepositoryId, options);

        Assert.NotEmpty(first.Hotspots.PrimaryRankings);
        Assert.NotEmpty(first.Hotspots.CompositeRankings);
        Assert.Contains(first.Hotspots.PrimaryRankings, x => x.TargetKind == HotspotTargetKind.Method);
        Assert.Contains(first.Hotspots.PrimaryRankings, x => x.TargetKind == HotspotTargetKind.Type);

        Assert.Equal(BuildFingerprint(first.Hotspots), BuildFingerprint(second.Hotspots));
    }

    [Fact]
    public async Task Query_ExposesEffectiveThresholdsAndWeights_WithOverrideSupport()
    {
        var fixture = await HotspotRankingFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var model = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                HotspotRanking = new HotspotRankingOptions
                {
                    TopN = 4,
                    CompositeWeightOverrides = new Dictionary<HotspotMetricKind, double>
                    {
                        [HotspotMetricKind.MethodCognitiveComplexity] = 5d,
                    },
                    ThresholdOverrides = new Dictionary<HotspotMetricKind, HotspotSeverityThresholds>
                    {
                        [HotspotMetricKind.MethodCyclomaticComplexity] = new HotspotSeverityThresholds(0.10d, 0.20d, 0.30d, 0.40d),
                    },
                },
            });

        var config = model.Hotspots.EffectiveConfig;
        Assert.Equal(5d, config.CompositeWeights[HotspotMetricKind.MethodCognitiveComplexity]);
        Assert.Equal(0.10d, config.Thresholds[HotspotMetricKind.MethodCyclomaticComplexity].Low);
        Assert.Equal(4, config.EffectiveTopN);
    }

    [Fact]
    public async Task Query_AppliesTopNByDefault_AndSupportsExplicitUnboundedOverride()
    {
        var fixture = await HotspotRankingFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var bounded = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                HotspotRanking = new HotspotRankingOptions
                {
                    TopN = 1,
                    Unbounded = false,
                },
            });

        Assert.All(bounded.Hotspots.PrimaryRankings, x => Assert.True(x.Rows.Count <= 1));
        Assert.All(bounded.Hotspots.CompositeRankings, x => Assert.True(x.Rows.Count <= 1));

        var unbounded = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                HotspotRanking = new HotspotRankingOptions
                {
                    TopN = 1,
                    Unbounded = true,
                },
            });

        Assert.Contains(unbounded.Hotspots.PrimaryRankings, x => x.Rows.Count > 1);
        Assert.Contains(unbounded.Hotspots.CompositeRankings, x => x.Rows.Count > 1);
    }

    [Fact]
    public async Task Query_UsesDefaultTopNFallback_WhenTopNIsNotConfigured()
    {
        var fixture = await HotspotRankingFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var nullTopN = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                HotspotRanking = new HotspotRankingOptions
                {
                    TopN = null,
                },
            });
        Assert.Equal(25, nullTopN.Hotspots.EffectiveConfig.EffectiveTopN);

        var zeroTopN = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
                HotspotRanking = new HotspotRankingOptions
                {
                    TopN = 0,
                },
            });
        Assert.Equal(25, zeroTopN.Hotspots.EffectiveConfig.EffectiveTopN);
    }

    [Fact]
    public async Task Render_GeneratesDedicatedHotspotPages_WithMinimalFrontMatterAndReadableTables()
    {
        var fixture = await HotspotRankingFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
            });

        var pages = new ProjectStructureWikiRenderer().Render(model);
        Assert.Contains(pages, x => x.RelativePath == "hotspots/methods.md");
        Assert.Contains(pages, x => x.RelativePath == "hotspots/types.md");
        Assert.Contains(pages, x => x.RelativePath == "hotspots/files.md");
        Assert.Contains(pages, x => x.RelativePath == "hotspots/namespaces.md");
        Assert.Contains(pages, x => x.RelativePath == "hotspots/projects.md");
        Assert.Contains(pages, x => x.RelativePath == "hotspots/repository.md");

        var methodsHotspot = pages.Single(x => x.RelativePath == "hotspots/methods.md");
        Assert.Contains("entity_type: hotspot", methodsHotspot.Markdown, StringComparison.Ordinal);
        Assert.Contains("hotspot_kind: methods", methodsHotspot.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("target_frameworks:", methodsHotspot.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("project_path:", methodsHotspot.Markdown, StringComparison.Ordinal);
        Assert.Contains("| rank | entity | raw_value | normalized_score | severity |", methodsHotspot.Markdown, StringComparison.Ordinal);
        Assert.Contains("](methods/", methodsHotspot.Markdown, StringComparison.Ordinal);

        var repositoryPage = pages.Single(x => x.RelativePath.StartsWith("repositories/", StringComparison.Ordinal));
        Assert.Contains("(hotspots/methods.md)", repositoryPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("(hotspots/repository.md)", repositoryPage.Markdown, StringComparison.Ordinal);
    }

    private static string BuildFingerprint(HotspotRankingCatalog catalog)
    {
        var primary = catalog.PrimaryRankings
            .SelectMany(ranking => ranking.Rows.Select(row => $"P:{ranking.TargetKind}:{ranking.MetricKind}:{row.EntityId.Value}:{row.Rank}:{row.NormalizedScore:F8}:{row.Severity}"));
        var composite = catalog.CompositeRankings
            .SelectMany(ranking => ranking.Rows.Select(row => $"C:{ranking.TargetKind}:{row.EntityId.Value}:{row.Rank}:{row.CompositeScore:F8}:{row.Severity}"));

        return string.Join("\n", primary.Concat(composite));
    }

    private sealed class HotspotRankingFixture
    {
        private HotspotRankingFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<HotspotRankingFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-hotspot-ranking-{Guid.NewGuid():N}", "metric-repo");
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
                    <AssemblyName>Acme.Hotspots</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Hotspots.cs"),
                """
                namespace Acme.Hotspots;

                public class DependencyA { }
                public class DependencyB { }
                public class DependencyC { }

                public class TypeAlpha
                {
                    public int Simple(int x)
                    {
                        return x + 1;
                    }

                    public int Branchy(int x)
                    {
                        if (x > 10)
                        {
                            return x;
                        }

                        if (x % 2 == 0)
                        {
                            return x * 2;
                        }

                        return x + 3;
                    }

                    public int Looping(int x)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            x += i;
                        }

                        return x;
                    }
                }

                public class TypeBeta
                {
                    private System.Collections.Generic.Dictionary<DependencyA, DependencyB> _map = new();

                    public int Compute(DependencyC dep, int value)
                    {
                        _ = dep;
                        if (value > 0 && value < 10)
                        {
                            return _map.Count + value;
                        }

                        return value;
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new HotspotRankingFixture(root);
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
