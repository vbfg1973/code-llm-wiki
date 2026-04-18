using System.Xml.Linq;
using System.Text.Json;
using System.Diagnostics;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed class ProjectStructureAnalyzer : IProjectStructureAnalyzer
{
    private readonly IStableIdGenerator _stableIdGenerator;

    public ProjectStructureAnalyzer(IStableIdGenerator stableIdGenerator)
    {
        _stableIdGenerator = stableIdGenerator;
    }

    public Task<ProjectStructureAnalysisResult> AnalyzeAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var triples = new List<SemanticTriple>();
        var diagnostics = new List<IngestionDiagnostic>();

        var fullRepositoryPath = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(fullRepositoryPath))
        {
            diagnostics.Add(new IngestionDiagnostic("repository:path:not-found", $"Repository path '{fullRepositoryPath}' does not exist."));
            return Task.FromResult(new ProjectStructureAnalysisResult(default, triples, diagnostics));
        }

        var repositoryId = _stableIdGenerator.Create(new EntityKey("repository", fullRepositoryPath));
        AddEntityTriples(triples, repositoryId, "repository", Path.GetFileName(fullRepositoryPath), ".");

        var solutions = DiscoverSolutions(fullRepositoryPath, diagnostics);
        var solutionProjectMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var solutionPath in solutions)
        {
            var relativeSolutionPath = ToRelativePath(fullRepositoryPath, solutionPath);
            var solutionId = _stableIdGenerator.Create(new EntityKey("solution", relativeSolutionPath));
            AddEntityTriples(triples, solutionId, "solution", Path.GetFileNameWithoutExtension(solutionPath), relativeSolutionPath);
            triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.Contains, new EntityNode(solutionId)));

            var discoveredProjects = DiscoverProjectsFromSolution(solutionPath, diagnostics);
            solutionProjectMap[solutionPath] = discoveredProjects;
        }

        var allProjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in Directory.EnumerateFiles(fullRepositoryPath, "*.csproj", SearchOption.AllDirectories))
        {
            allProjectPaths.Add(Path.GetFullPath(project));
        }

        foreach (var solutionProjects in solutionProjectMap.Values)
        {
            foreach (var projectPath in solutionProjects)
            {
                allProjectPaths.Add(projectPath);
            }
        }

        var orderedProjects = allProjectPaths
            .OrderBy(path => ToRelativePath(fullRepositoryPath, path), StringComparer.Ordinal)
            .ToArray();

        var projectIdByPath = new Dictionary<string, EntityId>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in orderedProjects)
        {
            var relativeProjectPath = ToRelativePath(fullRepositoryPath, projectPath);
            var projectId = _stableIdGenerator.Create(new EntityKey("project", relativeProjectPath));
            projectIdByPath[projectPath] = projectId;

            var discovery = DiscoverProjectMetadata(projectPath, diagnostics);
            AddEntityTriples(triples, projectId, "project", discovery.Name, relativeProjectPath);
            triples.Add(new SemanticTriple(new EntityNode(projectId), CorePredicates.DiscoveryMethod, new LiteralNode(discovery.Method)));
            triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.Contains, new EntityNode(projectId)));

            var resolvedByPackage = DiscoverResolvedPackages(projectPath, diagnostics);

            foreach (var package in discovery.DeclaredPackages.OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                var packageId = _stableIdGenerator.Create(new EntityKey("package", package.PackageId.ToLowerInvariant()));
                AddEntityTriples(triples, packageId, "package", package.PackageId, package.PackageId);
                triples.Add(new SemanticTriple(new EntityNode(projectId), CorePredicates.ReferencesPackage, new EntityNode(packageId)));

                if (!string.IsNullOrWhiteSpace(package.DeclaredVersion))
                {
                    triples.Add(new SemanticTriple(new EntityNode(packageId), CorePredicates.HasDeclaredVersion, new LiteralNode(package.DeclaredVersion)));
                }

                if (resolvedByPackage.TryGetValue(package.PackageId, out var resolvedVersion) && !string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    triples.Add(new SemanticTriple(new EntityNode(packageId), CorePredicates.HasResolvedVersion, new LiteralNode(resolvedVersion)));
                }
            }
        }

        foreach (var pair in solutionProjectMap.OrderBy(x => ToRelativePath(fullRepositoryPath, x.Key), StringComparer.Ordinal))
        {
            var relativeSolutionPath = ToRelativePath(fullRepositoryPath, pair.Key);
            var solutionId = _stableIdGenerator.Create(new EntityKey("solution", relativeSolutionPath));

            foreach (var projectPath in pair.Value.OrderBy(x => ToRelativePath(fullRepositoryPath, x), StringComparer.Ordinal))
            {
                if (projectIdByPath.TryGetValue(projectPath, out var projectId))
                {
                    triples.Add(new SemanticTriple(new EntityNode(solutionId), CorePredicates.Contains, new EntityNode(projectId)));
                }
            }
        }

        var solutionMemberPaths = BuildSolutionMemberPathSet(fullRepositoryPath, solutionProjectMap);
        var gitTrackedFiles = DiscoverGitTrackedHeadFiles(fullRepositoryPath, diagnostics);

        foreach (var relativeFilePath in gitTrackedFiles.OrderBy(x => x, StringComparer.Ordinal))
        {
            var fileId = _stableIdGenerator.Create(new EntityKey("file", relativeFilePath));
            AddEntityTriples(
                triples,
                fileId,
                "file",
                Path.GetFileName(relativeFilePath),
                relativeFilePath);

            triples.Add(new SemanticTriple(new EntityNode(fileId), CorePredicates.FileKind, new LiteralNode(ClassifyFile(relativeFilePath))));
            triples.Add(new SemanticTriple(
                new EntityNode(fileId),
                CorePredicates.IsSolutionMember,
                new LiteralNode(IsSolutionMember(relativeFilePath, solutionMemberPaths).ToString().ToLowerInvariant())));
            triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.Contains, new EntityNode(fileId)));
        }

        return Task.FromResult(new ProjectStructureAnalysisResult(repositoryId, triples, diagnostics));
    }

    private static void AddEntityTriples(
        List<SemanticTriple> triples,
        EntityId entityId,
        string entityType,
        string name,
        string path)
    {
        var subject = new EntityNode(entityId);
        triples.Add(new SemanticTriple(subject, CorePredicates.EntityType, new LiteralNode(entityType)));
        triples.Add(new SemanticTriple(subject, CorePredicates.HasName, new LiteralNode(name)));
        triples.Add(new SemanticTriple(subject, CorePredicates.HasPath, new LiteralNode(path)));
    }

    private static IReadOnlyList<string> DiscoverSolutions(string repositoryPath, List<IngestionDiagnostic> diagnostics)
    {
        var solutions = Directory.EnumerateFiles(repositoryPath, "*.sln", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repositoryPath, "*.slnx", SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (solutions.Length == 0)
        {
            diagnostics.Add(new IngestionDiagnostic("solution:not-found", "No solution files were found; continuing with repository project discovery."));
        }

        return solutions;
    }

    private static HashSet<string> DiscoverProjectsFromSolution(string solutionPath, List<IngestionDiagnostic> diagnostics)
    {
        return Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? DiscoverProjectsFromSlnx(solutionPath, diagnostics)
            : DiscoverProjectsFromSln(solutionPath, diagnostics);
    }

    private static HashSet<string> DiscoverProjectsFromSlnx(string slnxPath, List<IngestionDiagnostic> diagnostics)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var document = XDocument.Load(slnxPath);
            var root = document.Root;
            if (root is null)
            {
                return projects;
            }

            foreach (var project in root.Elements("Project"))
            {
                var path = project.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(slnxPath)!, path));
                projects.Add(fullPath);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IngestionDiagnostic("solution:parse:failed", $"Failed to parse slnx '{slnxPath}': {ex.Message}"));
        }

        return projects;
    }

    private static HashSet<string> DiscoverProjectsFromSln(string slnPath, List<IngestionDiagnostic> diagnostics)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var solution = SolutionFile.Parse(slnPath);
            foreach (var project in solution.ProjectsInOrder)
            {
                if (!project.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(slnPath)!, project.RelativePath));
                projects.Add(fullPath);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IngestionDiagnostic("solution:parse:failed", $"Failed to parse sln '{slnPath}': {ex.Message}"));
        }

        return projects;
    }

    private static ProjectDiscoveryResult DiscoverProjectMetadata(string projectPath, List<IngestionDiagnostic> diagnostics)
    {
        diagnostics.Add(new IngestionDiagnostic("project:discovery:msbuild", $"Attempting MSBuild evaluation for '{projectPath}'."));

        try
        {
            using var projectCollection = new ProjectCollection();
            var project = projectCollection.LoadProject(projectPath);
            var name = project.GetPropertyValue("AssemblyName");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(projectPath);
            }

            var declaredPackages = project.GetItems("PackageReference")
                .Select(item => new PackageReferenceInfo(
                    item.EvaluatedInclude.Trim(),
                    ReadVersion(item.GetMetadataValue("Version"))))
                .Where(x => !string.IsNullOrWhiteSpace(x.PackageId))
                .ToArray();

            return new ProjectDiscoveryResult(name, "msbuild", declaredPackages);
        }
        catch (Exception ex)
        {
            var fallbackName = Path.GetFileNameWithoutExtension(projectPath);
            var fallbackPackages = Array.Empty<PackageReferenceInfo>();
            try
            {
                var document = XDocument.Load(projectPath);
                var assemblyName = document.Descendants("AssemblyName").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    fallbackName = assemblyName;
                }

                fallbackPackages = document
                    .Descendants("PackageReference")
                    .Select(node =>
                    {
                        var include = node.Attribute("Include")?.Value
                            ?? node.Attribute("Update")?.Value
                            ?? string.Empty;

                        var version = node.Attribute("Version")?.Value
                            ?? node.Elements("Version").FirstOrDefault()?.Value;

                        return new PackageReferenceInfo(include.Trim(), ReadVersion(version));
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.PackageId))
                    .ToArray();
            }
            catch
            {
                // Keep filename fallback only.
            }

            diagnostics.Add(new IngestionDiagnostic("project:discovery:fallback", $"Project '{projectPath}' fell back from MSBuild discovery: {ex.Message}"));
            return new ProjectDiscoveryResult(fallbackName, "fallback", fallbackPackages);
        }
    }

    private static Dictionary<string, string> DiscoverResolvedPackages(string projectPath, List<IngestionDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");

        if (!File.Exists(assetsPath))
        {
            diagnostics.Add(new IngestionDiagnostic("package:resolved:not-available", $"Resolved package data not available for '{projectPath}'."));
            return result;
        }

        try
        {
            using var stream = File.OpenRead(assetsPath);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new IngestionDiagnostic("package:resolved:not-available", $"Resolved package libraries are missing in '{assetsPath}'."));
                return result;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var typeElement) ||
                    !string.Equals(typeElement.GetString(), "package", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var nameAndVersion = library.Name.Split('/', 2, StringSplitOptions.TrimEntries);
                if (nameAndVersion.Length != 2)
                {
                    continue;
                }

                result[nameAndVersion[0]] = nameAndVersion[1];
            }

            diagnostics.Add(new IngestionDiagnostic("package:resolved:available", $"Resolved package data loaded for '{projectPath}'."));
            return result;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IngestionDiagnostic("package:resolved:not-available", $"Failed to parse resolved package data for '{projectPath}': {ex.Message}"));
            return result;
        }
    }

    private static string? ReadVersion(string? version)
    {
        var value = version?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ToRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative;
    }

    private static HashSet<string> BuildSolutionMemberPathSet(
        string repositoryRoot,
        Dictionary<string, HashSet<string>> solutionProjectMap)
    {
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in solutionProjectMap)
        {
            var relativeSolutionPath = ToRelativePath(repositoryRoot, pair.Key);
            if (!relativeSolutionPath.StartsWith("../", StringComparison.Ordinal))
            {
                members.Add(relativeSolutionPath);
            }

            foreach (var projectPath in pair.Value)
            {
                var relativeProjectPath = ToRelativePath(repositoryRoot, projectPath);
                if (relativeProjectPath.StartsWith("../", StringComparison.Ordinal))
                {
                    continue;
                }

                members.Add(relativeProjectPath);

                var directory = Path.GetDirectoryName(relativeProjectPath)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    members.Add($"{directory.TrimEnd('/')}/");
                }
            }
        }

        return members;
    }

    private static HashSet<string> DiscoverGitTrackedHeadFiles(string repositoryRoot, List<IngestionDiagnostic> diagnostics)
    {
        var filesFromHead = RunGitWithNulDelimitedOutput(repositoryRoot, "ls-tree", "-r", "--name-only", "-z", "HEAD");
        if (filesFromHead.ExitCode == 0 && filesFromHead.Entries.Count > 0)
        {
            return filesFromHead.Entries;
        }

        diagnostics.Add(new IngestionDiagnostic(
            "file:head:not-available",
            "HEAD file listing unavailable, falling back to git index listing."));

        var filesFromIndex = RunGitWithNulDelimitedOutput(repositoryRoot, "ls-files", "-z");
        if (filesFromIndex.ExitCode == 0)
        {
            return filesFromIndex.Entries;
        }

        diagnostics.Add(new IngestionDiagnostic(
            "file:git:failed",
            $"Failed to list git-tracked files: {filesFromIndex.Error}"));

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static GitCommandResult RunGitWithNulDelimitedOutput(string repositoryRoot, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        using var outputStream = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(outputStream);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bytes = outputStream.ToArray();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
            {
                continue;
            }

            if (i > start)
            {
                var value = System.Text.Encoding.UTF8.GetString(bytes, start, i - start).Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    entries.Add(value);
                }
            }

            start = i + 1;
        }

        return new GitCommandResult(process.ExitCode, entries, error);
    }

    private static string ClassifyFile(string relativePath)
    {
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "dotnet-source",
            ".sln" or ".slnx" => "solution",
            ".csproj" => "project",
            ".props" or ".targets" => "msbuild",
            ".md" => "documentation",
            ".json" or ".yaml" or ".yml" => "configuration",
            _ => "other",
        };
    }

    private static bool IsSolutionMember(string relativeFilePath, HashSet<string> solutionMemberPaths)
    {
        if (solutionMemberPaths.Contains(relativeFilePath))
        {
            return true;
        }

        return solutionMemberPaths.Any(entry =>
            entry.EndsWith("/", StringComparison.Ordinal) &&
            relativeFilePath.StartsWith(entry, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProjectDiscoveryResult(string Name, string Method, IReadOnlyList<PackageReferenceInfo> DeclaredPackages);

    private sealed record PackageReferenceInfo(string PackageId, string? DeclaredVersion);

    private sealed record GitCommandResult(int ExitCode, HashSet<string> Entries, string Error);
}
