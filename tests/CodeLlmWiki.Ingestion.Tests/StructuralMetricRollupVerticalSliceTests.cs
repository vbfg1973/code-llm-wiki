using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class StructuralMetricRollupVerticalSliceTests
{
    [Fact]
    public async Task Query_BuildsDeterministicRollups_ForFileNamespaceProjectAndRepository()
    {
        var fixture = await StructuralMetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var first = query.GetModel(analysis.RepositoryId);
        var second = query.GetModel(analysis.RepositoryId);

        Assert.NotNull(first.StructuralMetrics.Repository);
        Assert.NotEmpty(first.StructuralMetrics.Files);
        Assert.NotEmpty(first.StructuralMetrics.Namespaces);
        Assert.NotEmpty(first.StructuralMetrics.Projects);

        var globalNamespace = first.StructuralMetrics.Namespaces.Single(x => x.Name == "(global)");
        Assert.True(globalNamespace.Direct.Coverage.TotalMethods > 0);
        Assert.True(globalNamespace.Recursive.Coverage.TotalMethods >= globalNamespace.Direct.Coverage.TotalMethods);

        var servicesNamespace = first.StructuralMetrics.Namespaces.Single(x => x.Name == "Acme.Services");
        Assert.True(servicesNamespace.Direct.Coverage.AnalyzableMethods > 0);
        Assert.True(servicesNamespace.Recursive.Coverage.AnalyzableMethods > servicesNamespace.Direct.Coverage.AnalyzableMethods);

        var firstFingerprint = BuildFingerprint(first.StructuralMetrics);
        var secondFingerprint = BuildFingerprint(second.StructuralMetrics);
        Assert.Equal(firstFingerprint, secondFingerprint);
    }

    [Fact]
    public async Task Query_UsesProductionDefaultRankingScope_AndSupportsFilterOverrides()
    {
        var fixture = await StructuralMetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var defaultModel = query.GetModel(analysis.RepositoryId);
        var defaultFiles = defaultModel.StructuralMetrics.Files;

        var productionFile = defaultFiles.Single(x => x.Path == "src/App/Services/Service.cs");
        var contestFile = defaultFiles.Single(x => x.Path == "src/App/Contest.cs");
        var generatedFile = defaultFiles.Single(x => x.Path == "src/App/Generated/Auto.generated.cs");
        var testFile = defaultFiles.Single(x => x.Path == "tests/App.Tests/ServiceTests.cs");

        Assert.Equal(StructuralMetricCodeKind.Production, productionFile.CodeKind);
        Assert.Equal(StructuralMetricCodeKind.Production, contestFile.CodeKind);
        Assert.Equal(StructuralMetricCodeKind.Generated, generatedFile.CodeKind);
        Assert.Equal(StructuralMetricCodeKind.Test, testFile.CodeKind);

        Assert.True(productionFile.Rollup.IncludedInRanking);
        Assert.False(generatedFile.Rollup.IncludedInRanking);
        Assert.False(testFile.Rollup.IncludedInRanking);
        Assert.Equal(3, defaultModel.StructuralMetrics.Repository.Rollup.Coverage.AnalyzableMethods);

        var defaultTestProject = defaultModel.StructuralMetrics.Projects.Single(x => x.Path == "tests/App.Tests/App.Tests.csproj");
        Assert.Equal(StructuralMetricSeverity.None, defaultTestProject.Rollup.Severity);
        Assert.False(defaultTestProject.Rollup.IncludedInRanking);

        var includeAllModel = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricScopeFilter = StructuralMetricScopeFilter.AllCodeKinds,
            });

        var includeAllFiles = includeAllModel.StructuralMetrics.Files;
        Assert.True(includeAllFiles.Single(x => x.Path == "src/App/Services/Service.cs").Rollup.IncludedInRanking);
        Assert.True(includeAllFiles.Single(x => x.Path == "src/App/Generated/Auto.generated.cs").Rollup.IncludedInRanking);
        Assert.True(includeAllFiles.Single(x => x.Path == "tests/App.Tests/ServiceTests.cs").Rollup.IncludedInRanking);
        Assert.Equal(5, includeAllModel.StructuralMetrics.Repository.Rollup.Coverage.AnalyzableMethods);

        var includeAllTestProject = includeAllModel.StructuralMetrics.Projects.Single(x => x.Path == "tests/App.Tests/App.Tests.csproj");
        Assert.Equal(1, includeAllTestProject.Rollup.Coverage.AnalyzableMethods);
        Assert.True(includeAllTestProject.Rollup.IncludedInRanking);
    }

    [Fact]
    public async Task Query_MarksInsufficientScopes_AsSeverityNone_AndExcludesThemFromDefaultRanking()
    {
        var fixture = await StructuralMetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var contractsFile = model.StructuralMetrics.Files.Single(x => x.Path == "src/App/Contracts/IContract.cs");
        Assert.Equal(StructuralMetricSeverity.None, contractsFile.Rollup.Severity);
        Assert.False(contractsFile.Rollup.IncludedInRanking);

        var contractsNamespace = model.StructuralMetrics.Namespaces.Single(x => x.Name == "Acme.Contracts");
        Assert.Equal(StructuralMetricSeverity.None, contractsNamespace.Direct.Severity);
        Assert.False(contractsNamespace.Direct.IncludedInRanking);
    }

    [Fact]
    public async Task Query_RollupsRemainDeterministic_AcrossConfiguredConcurrencyLevels()
    {
        var fixture = await StructuralMetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);

        var serialModel = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricComputationMaxDegreeOfParallelism = 1,
            });

        var parallelModel = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricComputationMaxDegreeOfParallelism = 4,
            });
        var secondParallelModel = query.GetModel(
            analysis.RepositoryId,
            new ProjectStructureQueryOptions
            {
                MetricComputationMaxDegreeOfParallelism = 4,
            });

        Assert.Equal(
            BuildFingerprint(serialModel.StructuralMetrics),
            BuildFingerprint(parallelModel.StructuralMetrics));
        Assert.Equal(
            BuildFingerprint(parallelModel.StructuralMetrics),
            BuildFingerprint(secondParallelModel.StructuralMetrics));
    }

    private static string BuildFingerprint(StructuralMetricRollupCatalog catalog)
    {
        static string SerializeRollup(StructuralMetricScopeRollup rollup)
            => $"{rollup.Coverage.TotalMethods}:{rollup.Coverage.AnalyzableMethods}:{rollup.Severity}:{rollup.IncludedInRanking}";

        var fileRows = catalog.Files
            .Select(x => $"file:{x.Path}:{x.CodeKind}:{SerializeRollup(x.Rollup)}");
        var namespaceRows = catalog.Namespaces
            .Select(x => $"ns:{x.Path}:{SerializeRollup(x.Direct)}:{SerializeRollup(x.Recursive)}");
        var projectRows = catalog.Projects
            .Select(x => $"project:{x.Path}:{x.CodeKind}:{SerializeRollup(x.Rollup)}");
        var repositoryRow = $"repo:{SerializeRollup(catalog.Repository.Rollup)}";

        return string.Join(
            "\n",
            fileRows.Concat(namespaceRows).Concat(projectRows).Append(repositoryRow));
    }

    private sealed class StructuralMetricFixture
    {
        private StructuralMetricFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<StructuralMetricFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-structural-rollups-{Guid.NewGuid():N}", "metric-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                  <Project Path="tests/App.Tests/App.Tests.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(appDir);
            await File.WriteAllTextAsync(Path.Combine(appDir, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "GlobalWorker.cs"),
                """
                public class GlobalWorker
                {
                    public int Execute(int value)
                    {
                        return value + 1;
                    }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Contest.cs"),
                """
                namespace Acme.Contest;

                public class ContestTag
                {
                }
                """);

            Directory.CreateDirectory(Path.Combine(appDir, "Services"));
            await File.WriteAllTextAsync(Path.Combine(appDir, "Services", "Service.cs"),
                """
                namespace Acme.Services;

                public class Service
                {
                    public int Run(int value)
                    {
                        if (value > 0)
                        {
                            return value;
                        }

                        return value + 1;
                    }
                }
                """);

            Directory.CreateDirectory(Path.Combine(appDir, "Services", "Internal"));
            await File.WriteAllTextAsync(Path.Combine(appDir, "Services", "Internal", "NestedService.cs"),
                """
                namespace Acme.Services.Internal;

                public class NestedService
                {
                    public int Compute(int value)
                    {
                        return value * 2;
                    }
                }
                """);

            Directory.CreateDirectory(Path.Combine(appDir, "Contracts"));
            await File.WriteAllTextAsync(Path.Combine(appDir, "Contracts", "IContract.cs"),
                """
                namespace Acme.Contracts;

                public interface IContract
                {
                    void Execute();
                }
                """);

            Directory.CreateDirectory(Path.Combine(appDir, "Generated"));
            await File.WriteAllTextAsync(Path.Combine(appDir, "Generated", "Auto.generated.cs"),
                """
                namespace Acme.Generated;

                public class AutoGenerated
                {
                    public int Value()
                    {
                        return 42;
                    }
                }
                """);

            var testsDir = Path.Combine(root, "tests", "App.Tests");
            Directory.CreateDirectory(testsDir);
            await File.WriteAllTextAsync(Path.Combine(testsDir, "App.Tests.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.App.Tests</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(testsDir, "ServiceTests.cs"),
                """
                namespace Acme.Tests;

                public class ServiceTests
                {
                    public int ShouldRun()
                    {
                        var service = new Acme.Services.Service();
                        return service.Run(3);
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new StructuralMetricFixture(root);
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
