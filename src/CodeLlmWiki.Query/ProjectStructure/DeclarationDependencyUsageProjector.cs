using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class DeclarationDependencyUsageProjector : IDeclarationDependencyUsageProjector
{
    public IReadOnlyDictionary<EntityId, PackageDeclarationDependencyUsageCatalog> Project(DeclarationDependencyUsageProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var metadataById = BuildMetadata(request.Triples);
        var methodById = request.Methods.ToDictionary(x => x.Id, x => x);
        var typeById = request.Types.ToDictionary(x => x.Id, x => x);
        var namespaceById = request.Namespaces.ToDictionary(x => x.Id, x => x);
        var packageById = request.Packages.ToDictionary(x => x.Id, x => x);
        var fileById = request.Files.ToDictionary(x => x.Id, x => x);

        var rows = request.Triples
            .Where(x => x.Predicate == CorePredicates.DependsOnTypeDeclaration)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => (MethodId: ((EntityNode)x.Subject).Id, TargetTypeId: ((EntityNode)x.Object).Id))
            .Where(x => methodById.ContainsKey(x.MethodId))
            .Select(x => BuildUsageRow(
                x.MethodId,
                x.TargetTypeId,
                methodById,
                typeById,
                namespaceById,
                packageById,
                fileById,
                request.Projects,
                metadataById))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        return rows
            .GroupBy(x => x.PackageId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var namespaces = x
                        .GroupBy(v => (v.NamespaceId, v.NamespaceName))
                        .Select(ns =>
                        {
                            var types = ns
                                .GroupBy(v => (v.TypeId, v.TypeName))
                                .Select(type =>
                                {
                                    var methods = type
                                        .GroupBy(v => (v.MethodId, v.MethodSignature))
                                        .Select(method => new PackageDeclarationDependencyMethodUsageNode(
                                            MethodId: method.Key.MethodId,
                                            MethodSignature: method.Key.MethodSignature,
                                            UsageCount: method.Sum(v => v.UsageCount)))
                                        .OrderByDescending(v => v.UsageCount)
                                        .ThenBy(v => v.MethodSignature, StringComparer.Ordinal)
                                        .ThenBy(v => v.MethodId.Value, StringComparer.Ordinal)
                                        .ToArray();

                                    return new PackageDeclarationDependencyTypeUsageNode(
                                        TypeId: type.Key.TypeId,
                                        TypeName: type.Key.TypeName,
                                        UsageCount: type.Sum(v => v.UsageCount),
                                        Methods: methods);
                                })
                                .OrderByDescending(v => v.UsageCount)
                                .ThenBy(v => v.TypeName, StringComparer.Ordinal)
                                .ThenBy(v => v.TypeId.Value, StringComparer.Ordinal)
                                .ToArray();

                            return new PackageDeclarationDependencyNamespaceUsageNode(
                                NamespaceId: ns.Key.NamespaceId,
                                NamespaceName: ns.Key.NamespaceName,
                                UsageCount: ns.Sum(v => v.UsageCount),
                                Types: types);
                        })
                        .OrderByDescending(v => v.UsageCount)
                        .ThenBy(v => v.NamespaceName, StringComparer.Ordinal)
                        .ThenBy(v => v.NamespaceId?.Value ?? string.Empty, StringComparer.Ordinal)
                        .ToArray();

                    return new PackageDeclarationDependencyUsageCatalog(
                        UsageCount: x.Sum(v => v.UsageCount),
                        Namespaces: namespaces);
                });
    }

    private static UsageRow? BuildUsageRow(
        EntityId methodId,
        EntityId targetTypeId,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, NamespaceDeclarationNode> namespaceById,
        IReadOnlyDictionary<EntityId, PackageNode> packageById,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        IReadOnlyList<ProjectNode> projects,
        IReadOnlyDictionary<EntityId, ProjectionMetadata> metadataById)
    {
        if (!methodById.TryGetValue(methodId, out var method)
            || !typeById.TryGetValue(method.DeclaringTypeId, out var declaringType)
            || !TryResolveMethodProject(method, fileById, projects, out var project)
            || !metadataById.TryGetValue(targetTypeId, out var targetTypeMeta))
        {
            return null;
        }

        if (!TryResolvePackage(project, packageById, targetTypeMeta.Name, out var package))
        {
            return null;
        }

        var namespaceName = declaringType.NamespaceId is { } namespaceId && namespaceById.TryGetValue(namespaceId, out var namespaceDeclaration)
            ? namespaceDeclaration.Name
            : "<global>";

        return new UsageRow(
            PackageId: package.Id,
            NamespaceId: declaringType.NamespaceId,
            NamespaceName: namespaceName,
            TypeId: declaringType.Id,
            TypeName: declaringType.Name,
            MethodId: method.Id,
            MethodSignature: method.Signature,
            UsageCount: 1);
    }

    private static bool TryResolveMethodProject(
        MethodDeclarationNode method,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        IReadOnlyList<ProjectNode> projects,
        out ProjectNode project)
    {
        var declarationFilePaths = method.DeclarationFileIds
            .Where(fileById.ContainsKey)
            .Select(id => fileById[id].Path)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var declarationFilePath in declarationFilePaths)
        {
            var candidate = projects
                .Where(p => IsFileWithinProject(p.Path, declarationFilePath))
                .OrderBy(p => p.Path, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Id.Value, StringComparer.Ordinal)
                .FirstOrDefault();

            if (candidate is not null)
            {
                project = candidate;
                return true;
            }
        }

        project = default!;
        return false;
    }

    private static bool IsFileWithinProject(string projectPath, string filePath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)?.Replace('\\', '/') ?? string.Empty;
        var normalizedFilePath = filePath.Replace('\\', '/');

        return normalizedFilePath.Equals(projectDirectory, StringComparison.Ordinal)
            || normalizedFilePath.StartsWith(projectDirectory + "/", StringComparison.Ordinal);
    }

    private static bool TryResolvePackage(
        ProjectNode sourceProject,
        IReadOnlyDictionary<EntityId, PackageNode> packageById,
        string referenceName,
        out PackageNode package)
    {
        var candidates = sourceProject.PackageIds
            .Where(packageById.ContainsKey)
            .Select(id => packageById[id])
            .Where(p => PackageMatchesReference(p, referenceName))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Id.Value, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 1)
        {
            package = candidates[0];
            return true;
        }

        package = default!;
        return false;
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

    private static Dictionary<EntityId, ProjectionMetadata> BuildMetadata(IReadOnlyList<SemanticTriple> triples)
    {
        var metadataById = new Dictionary<EntityId, ProjectionMetadata>();

        foreach (var triple in triples)
        {
            if (triple.Subject is not EntityNode subject || triple.Object is not LiteralNode literal)
            {
                continue;
            }

            if (!metadataById.TryGetValue(subject.Id, out var metadata))
            {
                metadata = new ProjectionMetadata();
                metadataById[subject.Id] = metadata;
            }

            var value = literal.Value?.ToString() ?? string.Empty;
            if (triple.Predicate == CorePredicates.HasName)
            {
                metadata.Name = value;
            }
            else if (triple.Predicate == CorePredicates.HasPath)
            {
                metadata.Path = value;
            }
            else if (triple.Predicate == CorePredicates.EntityType)
            {
                metadata.EntityType = value;
            }
        }

        return metadataById;
    }

    private sealed class ProjectionMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
    }

    private sealed record UsageRow(
        EntityId PackageId,
        EntityId? NamespaceId,
        string NamespaceName,
        EntityId TypeId,
        string TypeName,
        EntityId MethodId,
        string MethodSignature,
        int UsageCount);
}
