using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class ProjectScopedPackageAttributionResolver : IProjectScopedPackageAttributionResolver
{
    private readonly IReadOnlyDictionary<EntityId, MethodDeclarationNode> _methodById;
    private readonly IReadOnlyDictionary<EntityId, FileNode> _fileById;
    private readonly IReadOnlyDictionary<EntityId, PackageNode> _packageById;
    private readonly IReadOnlyList<ProjectNode> _projects;

    public ProjectScopedPackageAttributionResolver(
        IReadOnlyList<ProjectNode> projects,
        IReadOnlyList<PackageNode> packages,
        IReadOnlyList<FileNode> files,
        IReadOnlyList<MethodDeclarationNode> methods)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(methods);

        _projects = projects;
        _packageById = packages.ToDictionary(x => x.Id, x => x);
        _fileById = files.ToDictionary(x => x.Id, x => x);
        _methodById = methods.ToDictionary(x => x.Id, x => x);
    }

    public PackageAttributionResolution Resolve(EntityId sourceMethodId, string targetReferenceName)
    {
        if (!_methodById.TryGetValue(sourceMethodId, out var sourceMethod))
        {
            return new PackageAttributionResolution(
                Status: PackageAttributionStatus.Unknown,
                SourceProjectId: null,
                PackageId: null,
                Reason: PackageAttributionReason.SourceMethodNotFound,
                OrderedCandidatePackageIds: []);
        }

        if (!TryResolveSourceProject(sourceMethod, out var sourceProject))
        {
            return new PackageAttributionResolution(
                Status: PackageAttributionStatus.Unknown,
                SourceProjectId: null,
                PackageId: null,
                Reason: PackageAttributionReason.SourceProjectNotFound,
                OrderedCandidatePackageIds: []);
        }

        var candidates = sourceProject.PackageIds
            .Where(_packageById.ContainsKey)
            .Select(packageId => _packageById[packageId])
            .Where(package => PackageMatchesReference(package, targetReferenceName))
            .OrderBy(package => package.Name, StringComparer.Ordinal)
            .ThenBy(package => package.Id.Value, StringComparer.Ordinal)
            .Select(package => package.Id)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new PackageAttributionResolution(
                Status: PackageAttributionStatus.Unknown,
                SourceProjectId: sourceProject.Id,
                PackageId: null,
                Reason: PackageAttributionReason.NoProjectScopedMatch,
                OrderedCandidatePackageIds: []);
        }

        if (candidates.Length > 1)
        {
            return new PackageAttributionResolution(
                Status: PackageAttributionStatus.Unknown,
                SourceProjectId: sourceProject.Id,
                PackageId: null,
                Reason: PackageAttributionReason.AmbiguousProjectScopedMatch,
                OrderedCandidatePackageIds: candidates);
        }

        return new PackageAttributionResolution(
            Status: PackageAttributionStatus.Attributed,
            SourceProjectId: sourceProject.Id,
            PackageId: candidates[0],
            Reason: PackageAttributionReason.None,
            OrderedCandidatePackageIds: candidates);
    }

    private bool TryResolveSourceProject(MethodDeclarationNode sourceMethod, out ProjectNode sourceProject)
    {
        var declarationFilePaths = sourceMethod.DeclarationFileIds
            .Where(_fileById.ContainsKey)
            .Select(fileId => _fileById[fileId].Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var declarationFilePath in declarationFilePaths)
        {
            var candidate = _projects
                .Where(project => IsFileWithinProject(project.Path, declarationFilePath))
                .Select(project =>
                {
                    var projectDirectory = NormalizeProjectDirectory(project.Path);
                    return new
                    {
                        Project = project,
                        Directory = projectDirectory,
                    };
                })
                .OrderByDescending(x => x.Directory.Length)
                .ThenBy(x => x.Project.Path, StringComparer.Ordinal)
                .ThenBy(x => x.Project.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Project.Id.Value, StringComparer.Ordinal)
                .Select(x => x.Project)
                .FirstOrDefault();

            if (candidate is not null)
            {
                sourceProject = candidate;
                return true;
            }
        }

        sourceProject = default!;
        return false;
    }

    private static bool IsFileWithinProject(string projectPath, string filePath)
    {
        var projectDirectory = NormalizeProjectDirectory(projectPath);
        var normalizedFilePath = filePath.Replace('\\', '/');

        return normalizedFilePath.Equals(projectDirectory, StringComparison.Ordinal)
            || normalizedFilePath.StartsWith(projectDirectory + "/", StringComparison.Ordinal);
    }

    private static string NormalizeProjectDirectory(string projectPath)
    {
        return (Path.GetDirectoryName(projectPath) ?? string.Empty).Replace('\\', '/');
    }

    private static bool PackageMatchesReference(PackageNode package, string referenceName)
    {
        if (string.IsNullOrWhiteSpace(referenceName))
        {
            return false;
        }

        return referenceName.Equals(package.Name, StringComparison.OrdinalIgnoreCase)
               || referenceName.StartsWith(package.Name + ".", StringComparison.OrdinalIgnoreCase)
               || referenceName.Equals(package.CanonicalKey, StringComparison.OrdinalIgnoreCase)
               || referenceName.StartsWith(package.CanonicalKey + ".", StringComparison.OrdinalIgnoreCase);
    }
}
