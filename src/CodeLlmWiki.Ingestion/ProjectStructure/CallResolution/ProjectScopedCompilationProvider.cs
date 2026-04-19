using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;

public sealed class ProjectScopedCompilationProvider : IProjectScopedCompilationProvider
{
    public IProjectScopedSemanticContext Build(ProjectScopedCompilationRequest request, List<IngestionDiagnostic> diagnostics)
    {
        var orderedSourceFiles = request.SourceFiles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var syntaxTreeByRelativePath = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        foreach (var relativePath in orderedSourceFiles)
        {
            if (!request.SourceTextByRelativePath.TryGetValue(relativePath, out var sourceText))
            {
                var fullPath = Path.Combine(request.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                sourceText = File.ReadAllText(fullPath);
            }

            syntaxTreeByRelativePath[relativePath] = CSharpSyntaxTree.ParseText(sourceText, path: relativePath);
        }

        var allSyntaxTrees = syntaxTreeByRelativePath
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Value)
            .ToArray();

        var fallbackCompilation = CSharpCompilation.Create(
            assemblyName: "CodeLlmWiki.CallGraph.Fallback",
            syntaxTrees: allSyntaxTrees,
            references: request.References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var normalizedProjectPaths = request.ProjectPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var projectDirectoryByPath = normalizedProjectPaths.ToDictionary(
            x => x,
            x => Path.GetDirectoryName(x) ?? request.RepositoryRoot,
            StringComparer.OrdinalIgnoreCase);
        var orderedProjectPathsByDirectoryDepth = normalizedProjectPaths
            .OrderByDescending(x => projectDirectoryByPath[x].Length)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var owningProjectByRelativePath = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceFilesByOwningProject = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in orderedSourceFiles)
        {
            var owningProjectPath = ResolveOwningProjectPath(
                request.RepositoryRoot,
                relativePath,
                orderedProjectPathsByDirectoryDepth,
                projectDirectoryByPath);
            if (owningProjectPath is null)
            {
                continue;
            }

            owningProjectByRelativePath[relativePath] = owningProjectPath;
            if (!sourceFilesByOwningProject.TryGetValue(owningProjectPath, out var sourceFiles))
            {
                sourceFiles = [];
                sourceFilesByOwningProject[owningProjectPath] = sourceFiles;
            }

            sourceFiles.Add(relativePath);
        }

        var projectReferencesByProjectPath = normalizedProjectPaths.ToDictionary(
            x => x,
            x => ParseProjectReferences(x, diagnostics),
            StringComparer.OrdinalIgnoreCase);
        var projectSourceClosureByProjectPath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in normalizedProjectPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var closureProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectProjectReferenceClosure(projectPath, projectReferencesByProjectPath, closureProjects, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var closureSourceFiles = closureProjects
                .SelectMany(x => sourceFilesByOwningProject.TryGetValue(x, out var files) ? files : [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            projectSourceClosureByProjectPath[projectPath] = closureSourceFiles;
        }

        var projectCompilationByPath = new Dictionary<string, CSharpCompilation>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectPath in normalizedProjectPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var closureSourceFiles = projectSourceClosureByProjectPath.TryGetValue(projectPath, out var files)
                ? files
                : [];
            var syntaxTrees = closureSourceFiles
                .Where(syntaxTreeByRelativePath.ContainsKey)
                .Select(relativePath => syntaxTreeByRelativePath[relativePath])
                .ToArray();

            var assemblyName = request.ProjectAssemblyNameByPath.TryGetValue(projectPath, out var configuredAssemblyName)
                ? configuredAssemblyName
                : Path.GetFileNameWithoutExtension(projectPath);

            var compilation = CSharpCompilation.Create(
                assemblyName: string.IsNullOrWhiteSpace(assemblyName) ? "CodeLlmWiki.ProjectScoped" : assemblyName,
                syntaxTrees: syntaxTrees,
                references: request.References,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            projectCompilationByPath[projectPath] = compilation;
        }

        return new ProjectScopedSemanticContext(
            syntaxTreeByRelativePath,
            owningProjectByRelativePath,
            projectCompilationByPath,
            request.ProjectAssemblyNameByPath,
            fallbackCompilation);
    }

    private static string? ResolveOwningProjectPath(
        string repositoryRoot,
        string relativePath,
        IReadOnlyList<string> orderedProjectPathsByDirectoryDepth,
        IReadOnlyDictionary<string, string> projectDirectoryByPath)
    {
        var fullSourcePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var projectPath in orderedProjectPathsByDirectoryDepth)
        {
            var projectDirectory = projectDirectoryByPath[projectPath];
            if (fullSourcePath.StartsWith(projectDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullSourcePath.Equals(projectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return projectPath;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseProjectReferences(string projectPath, List<IngestionDiagnostic> diagnostics)
    {
        try
        {
            var projectDirectory = Path.GetDirectoryName(projectPath) ?? ".";
            var document = XDocument.Load(projectPath);

            var references = document
                .Descendants()
                .Where(x => string.Equals(x.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(x => x.Attribute("Include")?.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Path.GetFullPath(Path.Combine(projectDirectory, x!)))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return references;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "project:references:parse:failed",
                $"Failed to parse project references for '{projectPath}': {ex.Message}"));
            return [];
        }
    }

    private static void CollectProjectReferenceClosure(
        string projectPath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> projectReferencesByProjectPath,
        HashSet<string> closure,
        HashSet<string> active)
    {
        if (!closure.Add(projectPath))
        {
            return;
        }

        if (!active.Add(projectPath))
        {
            return;
        }

        if (projectReferencesByProjectPath.TryGetValue(projectPath, out var references))
        {
            foreach (var reference in references.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!projectReferencesByProjectPath.ContainsKey(reference))
                {
                    continue;
                }

                CollectProjectReferenceClosure(reference, projectReferencesByProjectPath, closure, active);
            }
        }

        active.Remove(projectPath);
    }

    private sealed class ProjectScopedSemanticContext : IProjectScopedSemanticContext
    {
        private readonly IReadOnlyDictionary<string, SyntaxTree> _syntaxTreeByRelativePath;
        private readonly IReadOnlyDictionary<string, string> _owningProjectByRelativePath;
        private readonly IReadOnlyDictionary<string, CSharpCompilation> _projectCompilationByPath;
        private readonly IReadOnlyDictionary<string, string> _projectAssemblyNameByPath;
        private readonly CSharpCompilation _fallbackCompilation;

        public ProjectScopedSemanticContext(
            IReadOnlyDictionary<string, SyntaxTree> syntaxTreeByRelativePath,
            IReadOnlyDictionary<string, string> owningProjectByRelativePath,
            IReadOnlyDictionary<string, CSharpCompilation> projectCompilationByPath,
            IReadOnlyDictionary<string, string> projectAssemblyNameByPath,
            CSharpCompilation fallbackCompilation)
        {
            _syntaxTreeByRelativePath = syntaxTreeByRelativePath;
            _owningProjectByRelativePath = owningProjectByRelativePath;
            _projectCompilationByPath = projectCompilationByPath;
            _projectAssemblyNameByPath = projectAssemblyNameByPath;
            _fallbackCompilation = fallbackCompilation;
        }

        public bool TryGetSemanticModel(string relativeSourceFilePath, out SemanticModel semanticModel, out SemanticContextInfo contextInfo)
        {
            if (!_syntaxTreeByRelativePath.TryGetValue(relativeSourceFilePath, out var syntaxTree))
            {
                semanticModel = null!;
                contextInfo = default;
                return false;
            }

            if (_owningProjectByRelativePath.TryGetValue(relativeSourceFilePath, out var owningProjectPath)
                && _projectCompilationByPath.TryGetValue(owningProjectPath, out var projectCompilation))
            {
                semanticModel = projectCompilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
                contextInfo = new SemanticContextInfo(
                    SemanticContextMode.ProjectScoped,
                    AssemblyName: _projectAssemblyNameByPath.TryGetValue(owningProjectPath, out var assemblyName)
                        ? assemblyName
                        : projectCompilation.AssemblyName ?? "UnknownAssembly",
                    ProjectPath: owningProjectPath);
                return true;
            }

            semanticModel = _fallbackCompilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            contextInfo = new SemanticContextInfo(
                SemanticContextMode.GlobalFallback,
                AssemblyName: _fallbackCompilation.AssemblyName ?? "CodeLlmWiki.CallGraph.Fallback",
                ProjectPath: null);
            return true;
        }
    }
}
