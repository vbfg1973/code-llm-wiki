using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class ProjectStructureQueryService : IProjectStructureQueryService
{
    private readonly IReadOnlyList<SemanticTriple> _triples;

    public ProjectStructureQueryService(IReadOnlyList<SemanticTriple> triples)
    {
        _triples = triples;
    }

    public ProjectStructureWikiModel GetModel(EntityId repositoryId)
    {
        var metadataById = BuildEntityMetadata(_triples);
        var contains = BuildContainsEdges(_triples);
        var packageReferences = BuildPackageReferences(_triples);
        var versionsByPackage = BuildPackageVersions(_triples);

        if (!metadataById.TryGetValue(repositoryId, out var repoMeta) || !repoMeta.IsType("repository"))
        {
            throw new InvalidOperationException($"Repository '{repositoryId}' was not found in graph triples.");
        }

        var solutionIds = contains
            .Where(x => x.Subject == repositoryId)
            .Select(x => x.Object)
            .Where(id => metadataById.TryGetValue(id, out var meta) && meta.IsType("solution"))
            .Distinct()
            .OrderBy(id => metadataById[id].Path, StringComparer.Ordinal)
            .ToArray();

        var solutions = solutionIds
            .Select(solutionId =>
            {
                var meta = metadataById[solutionId];
                var projectIds = contains
                    .Where(x => x.Subject == solutionId)
                    .Select(x => x.Object)
                    .Where(id => metadataById.TryGetValue(id, out var pMeta) && pMeta.IsType("project"))
                    .Distinct()
                    .OrderBy(id => metadataById[id].Path, StringComparer.Ordinal)
                    .ToArray();

                return new SolutionNode(solutionId, meta.Name, meta.Path, projectIds);
            })
            .ToArray();

        var projectIds = contains
            .Where(x => x.Subject == repositoryId)
            .Select(x => x.Object)
            .Where(id => metadataById.TryGetValue(id, out var meta) && meta.IsType("project"))
            .Concat(solutions.SelectMany(x => x.ProjectIds))
            .Distinct()
            .OrderBy(id => metadataById[id].Path, StringComparer.Ordinal)
            .ToArray();

        var projects = projectIds
            .Select(projectId =>
            {
                var meta = metadataById[projectId];
                var packageIds = packageReferences
                    .Where(x => x.ProjectId == projectId)
                    .Select(x => x.PackageId)
                    .Distinct()
                    .OrderBy(x => metadataById.TryGetValue(x, out var packageMeta) ? packageMeta.Name : x.Value, StringComparer.Ordinal)
                    .ToArray();

                return new ProjectNode(projectId, meta.Name, meta.Path, meta.DiscoveryMethod, packageIds);
            })
            .ToArray();

        var packageIds = projects
            .SelectMany(x => x.PackageIds)
            .Distinct()
            .OrderBy(x => metadataById.TryGetValue(x, out var packageMeta) ? packageMeta.Name : x.Value, StringComparer.Ordinal)
            .ToArray();

        var packages = packageIds
            .Select(packageId =>
            {
                var meta = metadataById.TryGetValue(packageId, out var packageMeta)
                    ? packageMeta
                    : new EntityMetadata(packageId);

                versionsByPackage.TryGetValue(packageId, out var versions);
                versions ??= new PackageVersionMetadata([], []);

                return new PackageNode(
                    packageId,
                    meta.Name,
                    versions.DeclaredVersions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    versions.ResolvedVersions.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            })
            .ToArray();

        var fileIds = contains
            .Where(x => x.Subject == repositoryId)
            .Select(x => x.Object)
            .Where(id => metadataById.TryGetValue(id, out var meta) && meta.IsType("file"))
            .Distinct()
            .OrderBy(id => metadataById[id].Path, StringComparer.Ordinal)
            .ToArray();

        var files = fileIds
            .Select(fileId =>
            {
                var meta = metadataById[fileId];
                return new FileNode(
                    fileId,
                    meta.Name,
                    meta.Path,
                    meta.FileKind,
                    meta.IsSolutionMember);
            })
            .ToArray();

        var repository = new RepositoryNode(repositoryId, repoMeta.Name, repoMeta.Path);
        return new ProjectStructureWikiModel(repository, solutions, projects, packages, files);
    }

    private static Dictionary<EntityId, EntityMetadata> BuildEntityMetadata(IReadOnlyList<SemanticTriple> triples)
    {
        var byId = new Dictionary<EntityId, EntityMetadata>();

        foreach (var triple in triples)
        {
            if (triple.Subject is not EntityNode subject)
            {
                continue;
            }

            if (triple.Object is not LiteralNode literal)
            {
                continue;
            }

            if (!byId.TryGetValue(subject.Id, out var meta))
            {
                meta = new EntityMetadata(subject.Id);
                byId[subject.Id] = meta;
            }

            var value = literal.Value?.ToString() ?? string.Empty;

            if (triple.Predicate == CorePredicates.EntityType)
            {
                meta.EntityType = value;
            }
            else if (triple.Predicate == CorePredicates.HasName)
            {
                meta.Name = value;
            }
            else if (triple.Predicate == CorePredicates.HasPath)
            {
                meta.Path = value;
            }
            else if (triple.Predicate == CorePredicates.DiscoveryMethod)
            {
                meta.DiscoveryMethod = value;
            }
            else if (triple.Predicate == CorePredicates.FileKind)
            {
                meta.FileKind = value;
            }
            else if (triple.Predicate == CorePredicates.IsSolutionMember)
            {
                meta.IsSolutionMember = bool.TryParse(value, out var parsed) && parsed;
            }
        }

        return byId;
    }

    private static IReadOnlyList<ContainsEdge> BuildContainsEdges(IReadOnlyList<SemanticTriple> triples)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.Contains)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new ContainsEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<PackageReferenceEdge> BuildPackageReferences(IReadOnlyList<SemanticTriple> triples)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.ReferencesPackage)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new PackageReferenceEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Distinct()
            .ToArray();
    }

    private static Dictionary<EntityId, PackageVersionMetadata> BuildPackageVersions(IReadOnlyList<SemanticTriple> triples)
    {
        var byPackageId = new Dictionary<EntityId, PackageVersionMetadata>();

        foreach (var triple in triples)
        {
            if (triple.Subject is not EntityNode subject || triple.Object is not LiteralNode literal)
            {
                continue;
            }

            if (triple.Predicate != CorePredicates.HasDeclaredVersion &&
                triple.Predicate != CorePredicates.HasResolvedVersion)
            {
                continue;
            }

            if (!byPackageId.TryGetValue(subject.Id, out var versions))
            {
                versions = new PackageVersionMetadata(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                byPackageId[subject.Id] = versions;
            }

            var value = literal.Value?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (triple.Predicate == CorePredicates.HasDeclaredVersion)
            {
                versions.DeclaredVersions.Add(value);
            }
            else
            {
                versions.ResolvedVersions.Add(value);
            }
        }

        return byPackageId;
    }

    private sealed class EntityMetadata
    {
        public EntityMetadata(EntityId id)
        {
            Id = id;
            Name = id.Value;
            Path = id.Value;
            DiscoveryMethod = string.Empty;
            FileKind = string.Empty;
            IsSolutionMember = false;
            EntityType = string.Empty;
        }

        public EntityId Id { get; }

        public string EntityType { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public string DiscoveryMethod { get; set; }

        public string FileKind { get; set; }

        public bool IsSolutionMember { get; set; }

        public bool IsType(string entityType) => EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ContainsEdge(EntityId Subject, EntityId Object);
    private sealed record PackageReferenceEdge(EntityId ProjectId, EntityId PackageId);

    private sealed record PackageVersionMetadata(
        HashSet<string> DeclaredVersions,
        HashSet<string> ResolvedVersions);
}
