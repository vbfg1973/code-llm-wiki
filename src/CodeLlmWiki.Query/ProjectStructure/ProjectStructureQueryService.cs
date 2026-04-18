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
        var packageReferences = BuildPackageReferences(_triples, metadataById);
        var projectPackageReferences = BuildProjectPackageReferences(_triples, metadataById);
        var packageReferenceTargets = BuildPackageReferenceTargets(_triples, metadataById);
        var packageReferenceVersions = BuildPackageReferenceVersions(_triples, metadataById);
        var versionsByPackage = BuildPackageVersions(_triples);
        var fileHistoryEdges = BuildFileHistoryEdges(_triples);
        var submoduleEdges = BuildSubmoduleEdges(_triples);

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

                return new ProjectNode(
                    projectId,
                    meta.Name,
                    meta.Path,
                    meta.DiscoveryMethod,
                    meta.TargetFrameworks.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    packageIds);
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

                var projectMemberships = projectPackageReferences
                    .Where(x => packageReferenceTargets.TryGetValue(x.PackageReferenceId, out var targetPackageId) && targetPackageId == packageId)
                    .Select(x =>
                    {
                        var projectMeta = metadataById[x.ProjectId];
                        packageReferenceVersions.TryGetValue(x.PackageReferenceId, out var versionMeta);

                        return new PackageProjectMembershipNode(
                            x.ProjectId,
                            projectMeta.Name,
                            projectMeta.Path,
                            versionMeta?.DeclaredVersion,
                            versionMeta?.ResolvedVersion);
                    })
                    .OrderBy(x => x.ProjectPath, StringComparer.Ordinal)
                    .ThenBy(x => x.ProjectName, StringComparer.Ordinal)
                    .ThenBy(x => x.ProjectId.Value, StringComparer.Ordinal)
                    .ToArray();

                return new PackageNode(
                    packageId,
                    meta.Name,
                    meta.Name.ToLowerInvariant(),
                    versions.DeclaredVersions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    versions.ResolvedVersions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    projectMemberships);
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
                var history = fileHistoryEdges
                    .Where(x => x.FileId == fileId)
                    .Select(x => x.EventId)
                    .Where(id => metadataById.TryGetValue(id, out var eventMeta) && eventMeta.IsType("file-history-event"))
                    .Select(id => metadataById[id])
                    .Select(eventMeta => new FileHistoryEntryNode(
                        eventMeta.CommitSha,
                        eventMeta.CommittedAtUtc,
                        eventMeta.AuthorName,
                        eventMeta.AuthorEmail))
                    .OrderByDescending(x => ParseTimestamp(x.TimestampUtc))
                    .ThenBy(x => x.CommitSha, StringComparer.Ordinal)
                    .ToArray();

                var mergeEvents = fileHistoryEdges
                    .Where(x => x.FileId == fileId)
                    .Select(x => x.EventId)
                    .Where(id => metadataById.TryGetValue(id, out var eventMeta)
                        && eventMeta.IsType("file-history-event")
                        && eventMeta.IsMergeToMainline)
                    .Select(id => metadataById[id])
                    .Select(eventMeta => new FileMergeEventNode(
                        eventMeta.CommitSha,
                        eventMeta.CommittedAtUtc,
                        eventMeta.AuthorName,
                        eventMeta.AuthorEmail,
                        eventMeta.TargetBranch,
                        eventMeta.SourceBranchFileCommitCount))
                    .OrderByDescending(x => ParseTimestamp(x.TimestampUtc))
                    .ThenBy(x => x.MergeCommitSha, StringComparer.Ordinal)
                    .ToArray();

                var lastChange = history.FirstOrDefault();

                return new FileNode(
                    fileId,
                    meta.Name,
                    meta.Path,
                    meta.FileKind,
                    meta.IsSolutionMember,
                    meta.EditCount > 0 ? meta.EditCount : history.Length,
                    lastChange,
                    history,
                    mergeEvents);
            })
            .ToArray();

        var submoduleIds = submoduleEdges
            .Where(x => x.RepositoryId == repositoryId)
            .Select(x => x.SubmoduleId)
            .Distinct()
            .OrderBy(x => metadataById.TryGetValue(x, out var submoduleMeta) ? submoduleMeta.Path : x.Value, StringComparer.Ordinal)
            .ToArray();

        var submodules = submoduleIds
            .Select(submoduleId =>
            {
                var meta = metadataById.TryGetValue(submoduleId, out var submoduleMeta)
                    ? submoduleMeta
                    : new EntityMetadata(submoduleId);

                return new SubmoduleNode(submoduleId, meta.Name, meta.Path, meta.SubmoduleUrl);
            })
            .ToArray();

        var repository = new RepositoryNode(
            repositoryId,
            repoMeta.Name,
            repoMeta.Path,
            repoMeta.HeadBranch,
            repoMeta.MainlineBranch);

        return new ProjectStructureWikiModel(repository, solutions, projects, packages, files, submodules);
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
            else if (triple.Predicate == CorePredicates.TargetFramework)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    meta.TargetFrameworks.Add(value);
                }
            }
            else if (triple.Predicate == CorePredicates.FileKind)
            {
                meta.FileKind = value;
            }
            else if (triple.Predicate == CorePredicates.IsSolutionMember)
            {
                meta.IsSolutionMember = bool.TryParse(value, out var parsed) && parsed;
            }
            else if (triple.Predicate == CorePredicates.HeadBranch)
            {
                meta.HeadBranch = value;
            }
            else if (triple.Predicate == CorePredicates.MainlineBranch)
            {
                meta.MainlineBranch = value;
            }
            else if (triple.Predicate == CorePredicates.EditCount)
            {
                meta.EditCount = int.TryParse(value, out var parsed) ? parsed : 0;
            }
            else if (triple.Predicate == CorePredicates.LastChangeCommitSha)
            {
                meta.LastChangeCommitSha = value;
            }
            else if (triple.Predicate == CorePredicates.LastChangedAtUtc)
            {
                meta.LastChangedAtUtc = value;
            }
            else if (triple.Predicate == CorePredicates.LastChangedBy)
            {
                meta.LastChangedBy = value;
            }
            else if (triple.Predicate == CorePredicates.CommitSha)
            {
                meta.CommitSha = value;
            }
            else if (triple.Predicate == CorePredicates.CommittedAtUtc)
            {
                meta.CommittedAtUtc = value;
            }
            else if (triple.Predicate == CorePredicates.AuthorName)
            {
                meta.AuthorName = value;
            }
            else if (triple.Predicate == CorePredicates.AuthorEmail)
            {
                meta.AuthorEmail = value;
            }
            else if (triple.Predicate == CorePredicates.IsMergeToMainline)
            {
                meta.IsMergeToMainline = bool.TryParse(value, out var parsed) && parsed;
            }
            else if (triple.Predicate == CorePredicates.TargetBranch)
            {
                meta.TargetBranch = value;
            }
            else if (triple.Predicate == CorePredicates.SourceBranchFileCommitCount)
            {
                meta.SourceBranchFileCommitCount = int.TryParse(value, out var parsed) ? parsed : 0;
            }
            else if (triple.Predicate == CorePredicates.SubmoduleUrl)
            {
                meta.SubmoduleUrl = value;
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

    private static IReadOnlyList<PackageReferenceEdge> BuildPackageReferences(
        IReadOnlyList<SemanticTriple> triples,
        IReadOnlyDictionary<EntityId, EntityMetadata> metadataById)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.ReferencesPackage)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new PackageReferenceEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Where(x => metadataById.TryGetValue(x.ProjectId, out var subjectMeta) && subjectMeta.IsType("project"))
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<ProjectPackageReferenceEdge> BuildProjectPackageReferences(
        IReadOnlyList<SemanticTriple> triples,
        IReadOnlyDictionary<EntityId, EntityMetadata> metadataById)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.HasPackageReference)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new ProjectPackageReferenceEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Where(x =>
                metadataById.TryGetValue(x.ProjectId, out var projectMeta)
                && projectMeta.IsType("project")
                && metadataById.TryGetValue(x.PackageReferenceId, out var packageReferenceMeta)
                && packageReferenceMeta.IsType("package-reference"))
            .Distinct()
            .ToArray();
    }

    private static Dictionary<EntityId, EntityId> BuildPackageReferenceTargets(
        IReadOnlyList<SemanticTriple> triples,
        IReadOnlyDictionary<EntityId, EntityMetadata> metadataById)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.ReferencesPackage)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new PackageReferenceTargetEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Where(x =>
                metadataById.TryGetValue(x.PackageReferenceId, out var subjectMeta)
                && subjectMeta.IsType("package-reference")
                && metadataById.TryGetValue(x.PackageId, out var packageMeta)
                && packageMeta.IsType("package"))
            .GroupBy(x => x.PackageReferenceId)
            .ToDictionary(x => x.Key, x => x.First().PackageId);
    }

    private static Dictionary<EntityId, PackageReferenceVersionMetadata> BuildPackageReferenceVersions(
        IReadOnlyList<SemanticTriple> triples,
        IReadOnlyDictionary<EntityId, EntityMetadata> metadataById)
    {
        var byReferenceId = new Dictionary<EntityId, PackageReferenceVersionMetadata>();

        foreach (var triple in triples)
        {
            if (triple.Subject is not EntityNode subject || triple.Object is not LiteralNode literal)
            {
                continue;
            }

            if (!metadataById.TryGetValue(subject.Id, out var subjectMeta) || !subjectMeta.IsType("package-reference"))
            {
                continue;
            }

            if (triple.Predicate != CorePredicates.HasDeclaredVersion &&
                triple.Predicate != CorePredicates.HasResolvedVersion)
            {
                continue;
            }

            if (!byReferenceId.TryGetValue(subject.Id, out var version))
            {
                version = new PackageReferenceVersionMetadata(null, null);
                byReferenceId[subject.Id] = version;
            }

            var value = literal.Value?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (triple.Predicate == CorePredicates.HasDeclaredVersion)
            {
                version.DeclaredVersion ??= value;
            }
            else
            {
                version.ResolvedVersion ??= value;
            }
        }

        return byReferenceId;
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

    private static IReadOnlyList<FileHistoryEdge> BuildFileHistoryEdges(IReadOnlyList<SemanticTriple> triples)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.HasHistoryEvent)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new FileHistoryEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<SubmoduleEdge> BuildSubmoduleEdges(IReadOnlyList<SemanticTriple> triples)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.HasSubmodule)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new SubmoduleEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
            .Distinct()
            .ToArray();
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private sealed class EntityMetadata
    {
        public EntityMetadata(EntityId id)
        {
            Id = id;
            EntityType = string.Empty;
            Name = id.Value;
            Path = id.Value;
            DiscoveryMethod = string.Empty;
            FileKind = string.Empty;
            IsSolutionMember = false;
            TargetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HeadBranch = string.Empty;
            MainlineBranch = string.Empty;
            EditCount = 0;
            LastChangeCommitSha = string.Empty;
            LastChangedAtUtc = string.Empty;
            LastChangedBy = string.Empty;
            CommitSha = string.Empty;
            CommittedAtUtc = string.Empty;
            AuthorName = string.Empty;
            AuthorEmail = string.Empty;
            IsMergeToMainline = false;
            TargetBranch = string.Empty;
            SourceBranchFileCommitCount = 0;
            SubmoduleUrl = string.Empty;
        }

        public EntityId Id { get; }

        public string EntityType { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public string DiscoveryMethod { get; set; }

        public string FileKind { get; set; }

        public bool IsSolutionMember { get; set; }

        public HashSet<string> TargetFrameworks { get; }

        public string HeadBranch { get; set; }

        public string MainlineBranch { get; set; }

        public int EditCount { get; set; }

        public string LastChangeCommitSha { get; set; }

        public string LastChangedAtUtc { get; set; }

        public string LastChangedBy { get; set; }

        public string CommitSha { get; set; }

        public string CommittedAtUtc { get; set; }

        public string AuthorName { get; set; }

        public string AuthorEmail { get; set; }

        public bool IsMergeToMainline { get; set; }

        public string TargetBranch { get; set; }

        public int SourceBranchFileCommitCount { get; set; }

        public string SubmoduleUrl { get; set; }

        public bool IsType(string entityType) => EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ContainsEdge(EntityId Subject, EntityId Object);
    private sealed record PackageReferenceEdge(EntityId ProjectId, EntityId PackageId);
    private sealed record ProjectPackageReferenceEdge(EntityId ProjectId, EntityId PackageReferenceId);
    private sealed record PackageReferenceTargetEdge(EntityId PackageReferenceId, EntityId PackageId);
    private sealed record FileHistoryEdge(EntityId FileId, EntityId EventId);
    private sealed record SubmoduleEdge(EntityId RepositoryId, EntityId SubmoduleId);

    private sealed record PackageVersionMetadata(
        HashSet<string> DeclaredVersions,
        HashSet<string> ResolvedVersions);

    private sealed class PackageReferenceVersionMetadata
    {
        public PackageReferenceVersionMetadata(string? declaredVersion, string? resolvedVersion)
        {
            DeclaredVersion = declaredVersion;
            ResolvedVersion = resolvedVersion;
        }

        public string? DeclaredVersion { get; set; }

        public string? ResolvedVersion { get; set; }
    }
}
