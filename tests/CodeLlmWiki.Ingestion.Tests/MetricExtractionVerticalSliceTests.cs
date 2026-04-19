using System.Diagnostics;
using System.Globalization;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MetricExtractionVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_EmitsMethodMetricFacts_AndCoverageForBodylessMethods()
    {
        var fixture = await MetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var analyzerMethod = model.Declarations.Methods.Declarations.Single(x => x.Name == "Analyze");
        var interfaceMethod = model.Declarations.Methods.Declarations.Single(x => x.Name == "Execute");
        var abstractMethod = model.Declarations.Methods.Declarations.Single(x => x.Name == "AbstractOp");
        var partialMethod = model.Declarations.Methods.Declarations.Single(x => x.Name == "Hook");

        Assert.True(TryGetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.CyclomaticComplexity, out var cyclomatic));
        Assert.True(int.TryParse(cyclomatic, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cyclomaticValue));
        Assert.Equal(4, cyclomaticValue);

        Assert.True(TryGetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.CognitiveComplexity, out var cognitive));
        Assert.True(int.TryParse(cognitive, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cognitiveValue));
        Assert.Equal(3, cognitiveValue);

        var halsteadDistinctOperators = int.Parse(GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadDistinctOperators), CultureInfo.InvariantCulture);
        var halsteadDistinctOperands = int.Parse(GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadDistinctOperands), CultureInfo.InvariantCulture);
        var halsteadTotalOperators = int.Parse(GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadTotalOperators), CultureInfo.InvariantCulture);
        var halsteadTotalOperands = int.Parse(GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadTotalOperands), CultureInfo.InvariantCulture);
        var halsteadVocabulary = int.Parse(GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadVocabulary), CultureInfo.InvariantCulture);
        var halsteadLength = int.Parse(GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadLength), CultureInfo.InvariantCulture);
        Assert.True(TryGetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.HalsteadVolume, out var halsteadVolume));
        Assert.DoesNotContain(',', halsteadVolume);
        Assert.True(double.TryParse(halsteadVolume, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var halsteadValue));
        Assert.True(halsteadValue > 0d);
        Assert.Equal(halsteadDistinctOperators + halsteadDistinctOperands, halsteadVocabulary);
        Assert.Equal(halsteadTotalOperators + halsteadTotalOperands, halsteadLength);

        Assert.True(TryGetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.LocTotalLines, out var locTotal));
        Assert.True(int.TryParse(locTotal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var locTotalValue));
        Assert.True(locTotalValue > 0);
        Assert.True(TryGetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.LocCodeLines, out var locCode));
        Assert.True(int.TryParse(locCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var locCodeValue));
        Assert.True(locCodeValue > 0);

        Assert.True(TryGetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.MaintainabilityIndex, out var mi));
        Assert.True(double.TryParse(mi, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var miValue));
        Assert.InRange(miValue, 0d, 100d);
        var expectedMi = CalculateExpectedMaintainabilityIndex(halsteadValue, cyclomaticValue, locCodeValue);
        Assert.Equal(expectedMi, miValue, precision: 12);

        Assert.Equal("analyzable", GetLiteral(analysis.Triples, analyzerMethod.Id, CorePredicates.MetricCoverageStatus));
        Assert.Equal("no_analyzable_body", GetLiteral(analysis.Triples, interfaceMethod.Id, CorePredicates.MetricCoverageStatus));
        Assert.Equal("no_analyzable_body", GetLiteral(analysis.Triples, abstractMethod.Id, CorePredicates.MetricCoverageStatus));
        Assert.Equal("analyzable", GetLiteral(analysis.Triples, partialMethod.Id, CorePredicates.MetricCoverageStatus));
    }

    [Fact]
    public async Task AnalyzeAsync_EmitsTypeCboFacts_WithGenericAndWrapperNormalization()
    {
        var fixture = await MetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var consumerType = model.Declarations.Types.Single(x => x.Name == "CouplingSubject");

        var declarationCbo = int.Parse(GetLiteral(analysis.Triples, consumerType.Id, CorePredicates.CboDeclaration), CultureInfo.InvariantCulture);
        var methodBodyCbo = int.Parse(GetLiteral(analysis.Triples, consumerType.Id, CorePredicates.CboMethodBody), CultureInfo.InvariantCulture);
        var totalCbo = int.Parse(GetLiteral(analysis.Triples, consumerType.Id, CorePredicates.CboTotal), CultureInfo.InvariantCulture);

        Assert.Equal(3, declarationCbo);
        Assert.Equal(3, methodBodyCbo);
        Assert.Equal(4, totalCbo);
    }

    [Fact]
    public async Task AnalyzeAsync_CboNormalization_UnwrapsArrayAndNullableWrappers()
    {
        var fixture = await MetricFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var wrapperType = model.Declarations.Types.Single(x => x.Name == "WrapperSubject");

        var declarationCbo = int.Parse(GetLiteral(analysis.Triples, wrapperType.Id, CorePredicates.CboDeclaration), CultureInfo.InvariantCulture);
        var methodBodyCbo = int.Parse(GetLiteral(analysis.Triples, wrapperType.Id, CorePredicates.CboMethodBody), CultureInfo.InvariantCulture);
        var totalCbo = int.Parse(GetLiteral(analysis.Triples, wrapperType.Id, CorePredicates.CboTotal), CultureInfo.InvariantCulture);

        Assert.Equal(2, declarationCbo);
        Assert.Equal(2, methodBodyCbo);
        Assert.Equal(2, totalCbo);
    }

    [Fact]
    public async Task AnalyzeAsync_EmitsCboFacts_WhenRepositoryContainsNoMethodDeclarations()
    {
        var fixture = await NoMethodCboFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var carrierType = model.Declarations.Types.Single(x => x.Name == "DataCarrier");

        Assert.True(TryGetLiteral(analysis.Triples, carrierType.Id, CorePredicates.CboDeclaration, out var declarationCbo));
        Assert.True(TryGetLiteral(analysis.Triples, carrierType.Id, CorePredicates.CboMethodBody, out var methodBodyCbo));
        Assert.True(TryGetLiteral(analysis.Triples, carrierType.Id, CorePredicates.CboTotal, out var totalCbo));

        Assert.True(int.Parse(declarationCbo, CultureInfo.InvariantCulture) >= 1);
        Assert.Equal(0, int.Parse(methodBodyCbo, CultureInfo.InvariantCulture));
        Assert.True(int.Parse(totalCbo, CultureInfo.InvariantCulture) >= 1);
    }

    private static double CalculateExpectedMaintainabilityIndex(double halsteadVolume, int cyclomaticComplexity, int locCodeLines)
    {
        var safeVolume = Math.Max(1d, halsteadVolume);
        var safeLoc = Math.Max(1d, locCodeLines);
        var index = (171d
                    - (5.2d * Math.Log(safeVolume))
                    - (0.23d * cyclomaticComplexity)
                    - (16.2d * Math.Log(safeLoc)))
                    * 100d
                    / 171d;

        return Math.Clamp(index, 0d, 100d);
    }

    private static bool TryGetLiteral(
        IReadOnlyList<SemanticTriple> triples,
        EntityId subjectId,
        PredicateId predicate,
        out string value)
    {
        value = triples
            .Where(x => x.Predicate == predicate)
            .Where(x => x.Subject is EntityNode subject && subject.Id == subjectId)
            .Select(x => x.Object as LiteralNode)
            .Where(x => x is not null)
            .Select(x => x!.Value?.ToString())
            .FirstOrDefault() ?? string.Empty;

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetLiteral(
        IReadOnlyList<SemanticTriple> triples,
        EntityId subjectId,
        PredicateId predicate)
    {
        if (!TryGetLiteral(triples, subjectId, predicate, out var value))
        {
            throw new Xunit.Sdk.XunitException($"Expected literal for '{predicate.Value}' on '{subjectId.Value}'.");
        }

        return value;
    }

    private sealed class MetricFixture
    {
        private MetricFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MetricFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-metrics-{Guid.NewGuid():N}", "metric-repo");
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
                    <AssemblyName>Acme.Metrics.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Metrics.cs"),
                """
                namespace Acme.Metrics;

                public interface IRunner
                {
                    void Execute();
                }

                public abstract class BaseRunner
                {
                    public abstract void AbstractOp();
                }

                public class DependencyA { }
                public class DependencyB { }
                public struct DependencyStruct { }

                public class Consumer
                {
                    public int Analyze(int x)
                    {
                        if (x > 10 && x < 20)
                        {
                            return x;
                        }

                        for (var i = 0; i < 2; i++)
                        {
                            x += i;
                        }

                        return x;
                    }
                }

                public class CouplingSubject
                {
                    private System.Collections.Generic.List<DependencyA> _values = new();

                    public CouplingSubject(DependencyB dependency)
                    {
                        _ = dependency;
                    }

                    public int Compute()
                    {
                        var map = new System.Collections.Generic.Dictionary<DependencyA, DependencyB>();
                        return map.Count;
                    }
                }

                public class WrapperSubject
                {
                    private DependencyA[] _array = [];
                    private DependencyStruct? _optional;

                    public int Compute()
                    {
                        _ = typeof(DependencyA[]);
                        _ = typeof(DependencyStruct?);
                        return 0;
                    }
                }

                public partial class PartialRunner
                {
                    partial void Hook();

                    public void Trigger()
                    {
                        Hook();
                    }
                }

                public partial class PartialRunner
                {
                    partial void Hook()
                    {
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MetricFixture(root);
        }
    }

    private sealed class NoMethodCboFixture
    {
        private NoMethodCboFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<NoMethodCboFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-metrics-nomethods-{Guid.NewGuid():N}", "metric-repo");
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
                    <AssemblyName>Acme.Metrics.NoMethods</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "DataCarrier.cs"),
                """
                namespace Acme.Metrics.NoMethods;

                public class DependencyA { }

                public class DataCarrier
                {
                    public DependencyA Value { get; set; } = new();
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new NoMethodCboFixture(root);
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
