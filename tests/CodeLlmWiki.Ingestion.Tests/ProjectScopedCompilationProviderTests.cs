using System.Diagnostics;
using CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;
using Microsoft.CodeAnalysis;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class ProjectScopedCompilationProviderTests
{
    [Fact]
    public async Task Build_ResolvesProjectScopedSemanticModel_ForOwnedSourceFile()
    {
        var fixture = await CompilationFixture.CreateAsync();
        var provider = new ProjectScopedCompilationProvider();
        var diagnostics = new List<IngestionDiagnostic>();

        var context = provider.Build(
            new ProjectScopedCompilationRequest(
                RepositoryRoot: fixture.RepositoryPath,
                SourceFiles: fixture.SourceFiles,
                SourceTextByRelativePath: fixture.SourceTextByRelativePath,
                ProjectAssemblyNameByPath: fixture.ProjectAssemblyNameByPath,
                ProjectPaths: fixture.ProjectPaths,
                References: BuildDefaultReferences()),
            diagnostics);

        var resolved = context.TryGetSemanticModel("src/App/Program.cs", out _, out var info);

        Assert.True(resolved);
        Assert.Equal(SemanticContextMode.ProjectScoped, info.Mode);
        Assert.Equal("Acme.Compilations.App", info.AssemblyName);
        Assert.EndsWith("src/App/App.csproj", info.ProjectPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_UsesFallbackGlobalSemanticModel_ForUnownedSourceFile()
    {
        var fixture = await CompilationFixture.CreateAsync();
        var provider = new ProjectScopedCompilationProvider();
        var diagnostics = new List<IngestionDiagnostic>();

        var context = provider.Build(
            new ProjectScopedCompilationRequest(
                RepositoryRoot: fixture.RepositoryPath,
                SourceFiles: fixture.SourceFiles,
                SourceTextByRelativePath: fixture.SourceTextByRelativePath,
                ProjectAssemblyNameByPath: fixture.ProjectAssemblyNameByPath,
                ProjectPaths: fixture.ProjectPaths,
                References: BuildDefaultReferences()),
            diagnostics);

        var resolved = context.TryGetSemanticModel("notes.cs", out _, out var info);

        Assert.True(resolved);
        Assert.Equal(SemanticContextMode.GlobalFallback, info.Mode);
        Assert.Equal("CodeLlmWiki.CallGraph.Fallback", info.AssemblyName);
        Assert.Null(info.ProjectPath);
    }

    private static IReadOnlyList<MetadataReference> BuildDefaultReferences()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(tpa))
        {
            return [];
        }

        return tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private sealed class CompilationFixture
    {
        private CompilationFixture(
            string repositoryPath,
            IReadOnlyList<string> sourceFiles,
            IReadOnlyDictionary<string, string> sourceTextByRelativePath,
            IReadOnlyList<string> projectPaths,
            IReadOnlyDictionary<string, string> projectAssemblyNameByPath)
        {
            RepositoryPath = repositoryPath;
            SourceFiles = sourceFiles;
            SourceTextByRelativePath = sourceTextByRelativePath;
            ProjectPaths = projectPaths;
            ProjectAssemblyNameByPath = projectAssemblyNameByPath;
        }

        public string RepositoryPath { get; }
        public IReadOnlyList<string> SourceFiles { get; }
        public IReadOnlyDictionary<string, string> SourceTextByRelativePath { get; }
        public IReadOnlyList<string> ProjectPaths { get; }
        public IReadOnlyDictionary<string, string> ProjectAssemblyNameByPath { get; }

        public static async Task<CompilationFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-psc-provider-{Guid.NewGuid():N}", "fixture-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                  <Project Path="src/Lib/Lib.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            var libDir = Path.Combine(root, "src", "Lib");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(libDir);

            var appProjectPath = Path.Combine(appDir, "App.csproj");
            var libProjectPath = Path.Combine(libDir, "Lib.csproj");

            await File.WriteAllTextAsync(appProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.Compilations.App</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Lib/Lib.csproj" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(libProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.Compilations.Lib</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            var appProgramPath = Path.Combine(appDir, "Program.cs");
            var libTypePath = Path.Combine(libDir, "Helper.cs");
            var notesPath = Path.Combine(root, "notes.cs");

            await File.WriteAllTextAsync(appProgramPath,
                """
                using Acme.Compilations.Lib;

                namespace Acme.Compilations.App;

                public static class Program
                {
                    public static void Run()
                    {
                        Helper.Ping();
                    }
                }
                """);

            await File.WriteAllTextAsync(libTypePath,
                """
                namespace Acme.Compilations.Lib;

                public static class Helper
                {
                    public static void Ping()
                    {
                    }
                }
                """);

            await File.WriteAllTextAsync(notesPath,
                """
                public static class Notes
                {
                    public static void Keep()
                    {
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            var sourceFiles = new[]
            {
                "notes.cs",
                "src/App/Program.cs",
                "src/Lib/Helper.cs",
            };

            var sourceTextByRelativePath = sourceFiles.ToDictionary(
                x => x,
                x => File.ReadAllText(Path.Combine(root, x.Replace('/', Path.DirectorySeparatorChar))),
                StringComparer.Ordinal);

            return new CompilationFixture(
                repositoryPath: root,
                sourceFiles: sourceFiles,
                sourceTextByRelativePath: sourceTextByRelativePath,
                projectPaths: [appProjectPath, libProjectPath],
                projectAssemblyNameByPath: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [appProjectPath] = "Acme.Compilations.App",
                    [libProjectPath] = "Acme.Compilations.Lib",
                });
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
