using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using System.Globalization;

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
        return GetModel(repositoryId, ProjectStructureQueryOptions.Default);
    }

    public ProjectStructureWikiModel GetModel(EntityId repositoryId, ProjectStructureQueryOptions options)
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

        var declarations = BuildDeclarationCatalog(_triples, metadataById, repositoryId);
        var declarationDependencyUsageByPackageId = new DeclarationDependencyUsageProjector().Project(
            new DeclarationDependencyUsageProjectionRequest(
                _triples,
                projects,
                packages,
                files,
                declarations.Namespaces,
                declarations.Types,
                declarations.Methods.Declarations));
        var declarationDependencyTargetFirstByPackageId = new DeclarationDependencyTargetFirstProjector().Project(
            new DeclarationDependencyUsageProjectionRequest(
                _triples,
                projects,
                packages,
                files,
                declarations.Namespaces,
                declarations.Types,
                declarations.Methods.Declarations));
        var methodBodyDependencyUsageByPackageId = new MethodBodyDependencyUsageProjector().Project(
            new MethodBodyDependencyUsageProjectionRequest(
                _triples,
                projects,
                packages,
                files,
                declarations.Namespaces,
                declarations.Types,
                declarations.Methods.Declarations));
        var methodBodyDependencyTargetFirstByPackageId = new MethodBodyDependencyTargetFirstProjector().Project(
            new MethodBodyDependencyUsageProjectionRequest(
                _triples,
                projects,
                packages,
                files,
                declarations.Namespaces,
                declarations.Types,
                declarations.Methods.Declarations));
        var declarationUnknownDependencyUsage = new UnknownDependencyUsageProjector().Project(
            new UnknownDependencyUsageProjectionRequest(
                CorePredicates.DependsOnTypeDeclaration,
                _triples,
                projects,
                packages,
                files,
                declarations.Namespaces,
                declarations.Types,
                declarations.Methods.Declarations));
        var methodBodyUnknownDependencyUsage = new UnknownDependencyUsageProjector().Project(
            new UnknownDependencyUsageProjectionRequest(
                CorePredicates.DependsOnTypeInMethodBody,
                _triples,
                projects,
                packages,
                files,
                declarations.Namespaces,
                declarations.Types,
                declarations.Methods.Declarations));
        packages = packages
            .Select(package => package with
            {
                DeclarationDependencyUsage = declarationDependencyUsageByPackageId.TryGetValue(package.Id, out var usage)
                    ? usage
                    : PackageDeclarationDependencyUsageCatalog.Empty,
                DeclarationDependencyTargetFirst = declarationDependencyTargetFirstByPackageId.TryGetValue(package.Id, out var targetFirstUsage)
                    ? targetFirstUsage
                    : PackageDeclarationDependencyTargetFirstCatalog.Empty,
                MethodBodyDependencyUsage = methodBodyDependencyUsageByPackageId.TryGetValue(package.Id, out var methodBodyUsage)
                    ? methodBodyUsage
                    : PackageMethodBodyDependencyUsageCatalog.Empty,
                MethodBodyDependencyTargetFirst = methodBodyDependencyTargetFirstByPackageId.TryGetValue(package.Id, out var methodBodyTargetFirstUsage)
                    ? methodBodyTargetFirstUsage
                    : PackageMethodBodyDependencyTargetFirstCatalog.Empty,
            })
            .ToArray();
        var typeDependencyRollupsByTypeId = BuildTypeDependencyRollupsByTypeId(
            packages,
            declarationUnknownDependencyUsage,
            methodBodyUnknownDependencyUsage);
        declarations = declarations with
        {
            Types = declarations.Types
                .Select(type => type with
                {
                    DependencyRollup = typeDependencyRollupsByTypeId.TryGetValue(type.Id, out var rollup)
                        ? rollup
                        : TypeDependencyRollupCatalog.Empty,
                })
                .ToArray(),
        };

        var repository = new RepositoryNode(
            repositoryId,
            repoMeta.Name,
            repoMeta.Path,
            repoMeta.HeadBranch,
            repoMeta.MainlineBranch);
        var structuralMetrics = new StructuralMetricRollupProjector().Project(
            new StructuralMetricRollupProjectionRequest(
                repositoryId,
                _triples,
                projects,
                files,
                declarations,
                options.MetricScopeFilter,
                options.MetricComputationMaxDegreeOfParallelism));
        var hotspots = new HotspotRankingProjector().Project(
            new HotspotRankingProjectionRequest(
                _triples,
                declarations,
                structuralMetrics,
                options.HotspotRanking,
                options.MetricComputationMaxDegreeOfParallelism));

        return new ProjectStructureWikiModel(repository, solutions, projects, packages, files, submodules)
        {
            Declarations = declarations,
            DependencyAttribution = new DependencyAttributionCatalog(
                declarationUnknownDependencyUsage,
                methodBodyUnknownDependencyUsage),
            StructuralMetrics = structuralMetrics,
            Hotspots = hotspots,
        };
    }

    private static IReadOnlyDictionary<EntityId, TypeDependencyRollupCatalog> BuildTypeDependencyRollupsByTypeId(
        IReadOnlyList<PackageNode> packages,
        UnknownDependencyUsageCatalog declarationUnknownDependencyUsage,
        UnknownDependencyUsageCatalog methodBodyUnknownDependencyUsage)
    {
        var packageById = packages.ToDictionary(package => package.Id, package => package);
        var declarationCounts = new Dictionary<(EntityId TypeId, EntityId PackageId), int>();
        var methodBodyCounts = new Dictionary<(EntityId TypeId, EntityId PackageId), int>();
        var declarationUnknownCounts = new Dictionary<EntityId, int>();
        var methodBodyUnknownCounts = new Dictionary<EntityId, int>();

        foreach (var package in packages)
        {
            foreach (var namespaceUsage in package.DeclarationDependencyUsage.Namespaces)
            {
                foreach (var typeUsage in namespaceUsage.Types)
                {
                    var key = (typeUsage.TypeId, package.Id);
                    declarationCounts[key] = declarationCounts.TryGetValue(key, out var existing)
                        ? existing + typeUsage.UsageCount
                        : typeUsage.UsageCount;
                }
            }

            foreach (var namespaceUsage in package.MethodBodyDependencyUsage.Namespaces)
            {
                foreach (var typeUsage in namespaceUsage.Types)
                {
                    var key = (typeUsage.TypeId, package.Id);
                    methodBodyCounts[key] = methodBodyCounts.TryGetValue(key, out var existing)
                        ? existing + typeUsage.UsageCount
                        : typeUsage.UsageCount;
                }
            }
        }

        foreach (var namespaceUsage in declarationUnknownDependencyUsage.Namespaces)
        {
            foreach (var typeUsage in namespaceUsage.Types)
            {
                declarationUnknownCounts[typeUsage.TypeId] = declarationUnknownCounts.TryGetValue(typeUsage.TypeId, out var existing)
                    ? existing + typeUsage.UsageCount
                    : typeUsage.UsageCount;
            }
        }

        foreach (var namespaceUsage in methodBodyUnknownDependencyUsage.Namespaces)
        {
            foreach (var typeUsage in namespaceUsage.Types)
            {
                methodBodyUnknownCounts[typeUsage.TypeId] = methodBodyUnknownCounts.TryGetValue(typeUsage.TypeId, out var existing)
                    ? existing + typeUsage.UsageCount
                    : typeUsage.UsageCount;
            }
        }

        var typeIds = declarationCounts.Keys.Select(x => x.TypeId)
            .Concat(methodBodyCounts.Keys.Select(x => x.TypeId))
            .Concat(declarationUnknownCounts.Keys)
            .Concat(methodBodyUnknownCounts.Keys)
            .Distinct()
            .OrderBy(x => x.Value, StringComparer.Ordinal)
            .ToArray();

        var rollups = new Dictionary<EntityId, TypeDependencyRollupCatalog>();
        foreach (var typeId in typeIds)
        {
            var declarationPackages = declarationCounts
                .Where(x => x.Key.TypeId == typeId && packageById.ContainsKey(x.Key.PackageId))
                .Select(x =>
                {
                    var package = packageById[x.Key.PackageId];
                    return new TypeDependencyPackageUsageNode(
                        PackageId: package.Id,
                        PackageName: package.Name,
                        UsageCount: x.Value);
                })
                .OrderByDescending(x => x.UsageCount)
                .ThenBy(x => x.PackageName, StringComparer.Ordinal)
                .ThenBy(x => x.PackageId.Value, StringComparer.Ordinal)
                .ToArray();

            var methodBodyPackages = methodBodyCounts
                .Where(x => x.Key.TypeId == typeId && packageById.ContainsKey(x.Key.PackageId))
                .Select(x =>
                {
                    var package = packageById[x.Key.PackageId];
                    return new TypeDependencyPackageUsageNode(
                        PackageId: package.Id,
                        PackageName: package.Name,
                        UsageCount: x.Value);
                })
                .OrderByDescending(x => x.UsageCount)
                .ThenBy(x => x.PackageName, StringComparer.Ordinal)
                .ThenBy(x => x.PackageId.Value, StringComparer.Ordinal)
                .ToArray();

            declarationUnknownCounts.TryGetValue(typeId, out var declarationUnknownCount);
            methodBodyUnknownCounts.TryGetValue(typeId, out var methodBodyUnknownCount);

            rollups[typeId] = new TypeDependencyRollupCatalog(
                DeclarationPackages: declarationPackages,
                MethodBodyPackages: methodBodyPackages,
                DeclarationUnknownUsageCount: declarationUnknownCount,
                MethodBodyUnknownUsageCount: methodBodyUnknownCount);
        }

        return rollups;
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
            else if (triple.Predicate == CorePredicates.TypeKind)
            {
                meta.TypeKind = value;
            }
            else if (triple.Predicate == CorePredicates.Accessibility)
            {
                meta.Accessibility = value;
            }
            else if (triple.Predicate == CorePredicates.Arity)
            {
                meta.Arity = int.TryParse(value, out var parsed) ? parsed : 0;
            }
            else if (triple.Predicate == CorePredicates.IsPartialType)
            {
                meta.IsPartialType = bool.TryParse(value, out var parsed) && parsed;
            }
            else if (triple.Predicate == CorePredicates.GenericParameter)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    meta.GenericParameters.Add(value);
                }
            }
            else if (triple.Predicate == CorePredicates.GenericConstraint)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    meta.GenericConstraints.Add(value);
                }
            }
            else if (triple.Predicate == CorePredicates.MemberKind)
            {
                meta.MemberKind = value;
            }
            else if (triple.Predicate == CorePredicates.MethodKind)
            {
                meta.MethodKind = value;
            }
            else if (triple.Predicate == CorePredicates.HasDeclaredTypeText)
            {
                meta.DeclaredTypeText = value;
            }
            else if (triple.Predicate == CorePredicates.HasReturnTypeText)
            {
                meta.ReturnTypeText = value;
            }
            else if (triple.Predicate == CorePredicates.ConstantValue)
            {
                meta.ConstantValue = value;
            }
            else if (triple.Predicate == CorePredicates.IsExtensionMethod)
            {
                meta.IsExtensionMethod = bool.TryParse(value, out var parsed) && parsed;
            }
            else if (triple.Predicate == CorePredicates.ParameterOrdinal)
            {
                meta.ParameterOrdinal = int.TryParse(value, out var parsed) ? parsed : -1;
            }
            else if (triple.Predicate == CorePredicates.ParameterName)
            {
                meta.ParameterName = value;
            }
            else if (triple.Predicate == CorePredicates.ExternalAssemblyName)
            {
                meta.ExternalAssemblyName = value;
            }
            else if (triple.Predicate == CorePredicates.ResolutionReason)
            {
                meta.ResolutionReason = value;
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

    private static DeclarationCatalog BuildDeclarationCatalog(
        IReadOnlyList<SemanticTriple> triples,
        IReadOnlyDictionary<EntityId, EntityMetadata> metadataById,
        EntityId repositoryId)
    {
        var namespaceContainment = BuildEntityEdges(triples, CorePredicates.ContainsNamespace);
        var typeContainment = BuildEntityEdges(triples, CorePredicates.ContainsType);
        var fileDeclaresNamespace = BuildEntityEdges(triples, CorePredicates.DeclaresNamespace);
        var fileDeclaresType = BuildEntityEdges(triples, CorePredicates.DeclaresType);
        var inheritsEdges = BuildEntityEdges(triples, CorePredicates.Inherits);
        var implementsEdges = BuildEntityEdges(triples, CorePredicates.Implements);
        var declaringTypeEdges = BuildEntityEdges(triples, CorePredicates.HasDeclaringType);
        var containsMemberEdges = BuildEntityEdges(triples, CorePredicates.ContainsMember);
        var containsMethodEdges = BuildEntityEdges(triples, CorePredicates.ContainsMethod);
        var declaredTypeEdges = BuildEntityEdges(triples, CorePredicates.HasDeclaredType);
        var methodReturnTypeEdges = BuildEntityEdges(triples, CorePredicates.HasReturnType);
        var methodParameterEdges = BuildEntityEdges(triples, CorePredicates.HasMethodParameter);
        var fileDeclaresMember = BuildEntityEdges(triples, CorePredicates.DeclaresMember);
        var fileDeclaresMethod = BuildEntityEdges(triples, CorePredicates.DeclaresMethod);
        var methodImplementsEdges = BuildEntityEdges(triples, CorePredicates.ImplementsMethod);
        var methodOverridesEdges = BuildEntityEdges(triples, CorePredicates.OverridesMethod);
        var methodCallsEdges = BuildEntityEdges(triples, CorePredicates.Calls);
        var methodReadsPropertyEdges = BuildEntityEdges(triples, CorePredicates.ReadsProperty);
        var methodWritesPropertyEdges = BuildEntityEdges(triples, CorePredicates.WritesProperty);
        var methodReadsFieldEdges = BuildEntityEdges(triples, CorePredicates.ReadsField);
        var methodWritesFieldEdges = BuildEntityEdges(triples, CorePredicates.WritesField);
        var methodExtendsTypeEdges = BuildEntityEdges(triples, CorePredicates.ExtendsType);
        var declarationLocationsByEntityId = BuildDeclarationLocationsByEntityId(triples);
        var methodCoverageByMethodId = BuildStringLiteralMap(triples, CorePredicates.MetricCoverageStatus);
        var cyclomaticByMethodId = BuildIntLiteralMap(triples, CorePredicates.CyclomaticComplexity);
        var cognitiveByMethodId = BuildIntLiteralMap(triples, CorePredicates.CognitiveComplexity);
        var halsteadByMethodId = BuildDoubleLiteralMap(triples, CorePredicates.HalsteadVolume);
        var maintainabilityByMethodId = BuildDoubleLiteralMap(triples, CorePredicates.MaintainabilityIndex);
        var cboDeclarationByTypeId = BuildIntLiteralMap(triples, CorePredicates.CboDeclaration);
        var cboMethodBodyByTypeId = BuildIntLiteralMap(triples, CorePredicates.CboMethodBody);
        var cboTotalByTypeId = BuildIntLiteralMap(triples, CorePredicates.CboTotal);

        var namespaceIds = metadataById
            .Where(x => x.Value.IsType("namespace"))
            .Select(x => x.Key)
            .Concat(namespaceContainment.Select(x => x.Object))
            .Distinct()
            .OrderBy(x => metadataById.TryGetValue(x, out var meta) ? meta.Name : x.Value, StringComparer.Ordinal)
            .ToArray();

        var parentByNamespace = new Dictionary<EntityId, EntityId>();
        foreach (var edge in namespaceContainment)
        {
            if (edge.Subject == repositoryId)
            {
                continue;
            }

            if (!metadataById.TryGetValue(edge.Subject, out var parentMeta) || !parentMeta.IsType("namespace"))
            {
                continue;
            }

            parentByNamespace[edge.Object] = edge.Subject;
        }

        var childrenByNamespace = namespaceContainment
            .Where(x => metadataById.TryGetValue(x.Subject, out var parentMeta) && parentMeta.IsType("namespace"))
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Name : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var typeNamespaceMap = typeContainment
            .Where(x => metadataById.TryGetValue(x.Subject, out var namespaceMeta) && namespaceMeta.IsType("namespace"))
            .Where(x => metadataById.TryGetValue(x.Object, out var typeMeta) && typeMeta.IsType("type-declaration"))
            .ToArray();

        var typeIds = typeNamespaceMap
            .Select(x => x.Object)
            .Concat(metadataById.Where(x => x.Value.IsType("type-declaration")).Select(x => x.Key))
            .Distinct()
            .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
            .ToArray();

        var namespaceByTypeId = typeNamespaceMap
            .GroupBy(x => x.Object)
            .ToDictionary(x => x.Key, x => x.First().Subject);

        var containedTypesByNamespace = typeNamespaceMap
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Name : id.Value, StringComparer.Ordinal)
                    .ThenBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var declarationFilesByNamespace = fileDeclaresNamespace
            .GroupBy(x => x.Object)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Subject)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var declarationFilesByType = fileDeclaresType
            .GroupBy(x => x.Object)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Subject)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var memberIdsByType = containsMemberEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Name : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var declaringTypeByMember = containsMemberEdges
            .GroupBy(x => x.Object)
            .ToDictionary(x => x.Key, x => x.First().Subject);

        var declarationFilesByMember = fileDeclaresMember
            .GroupBy(x => x.Object)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Subject)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var methodIdsByType = containsMethodEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Name : id.Value, StringComparer.Ordinal)
                    .ThenBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var declaringTypeByMethod = containsMethodEdges
            .GroupBy(x => x.Object)
            .ToDictionary(x => x.Key, x => x.First().Subject);

        var declarationFilesByMethod = fileDeclaresMethod
            .GroupBy(x => x.Object)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Subject)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var declaredTypeByEntity = declaredTypeEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(x => x.Key, x => x.First().Object);

        var returnTypeByMethod = methodReturnTypeEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(x => x.Key, x => x.First().Object);

        var parameterIdsByMethod = methodParameterEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.ParameterOrdinal : int.MaxValue)
                    .ThenBy(id => metadataById.TryGetValue(id, out var meta) ? meta.ParameterName : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var extendedTypeByMethod = methodExtendsTypeEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(x => x.Key, x => x.First().Object);

        var directBasesByType = inheritsEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var directInterfacesByType = implementsEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Object)
                    .Distinct()
                    .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
                    .ToArray());

        var declaringTypeByType = declaringTypeEdges
            .GroupBy(x => x.Subject)
            .ToDictionary(x => x.Key, x => x.First().Object);

        var namespaces = namespaceIds
            .Select(namespaceId =>
            {
                var meta = metadataById.TryGetValue(namespaceId, out var namespaceMeta)
                    ? namespaceMeta
                    : new EntityMetadata(namespaceId);

                parentByNamespace.TryGetValue(namespaceId, out var parentNamespaceId);
                childrenByNamespace.TryGetValue(namespaceId, out var childNamespaceIds);
                childNamespaceIds ??= [];

                containedTypesByNamespace.TryGetValue(namespaceId, out var containedTypeIds);
                containedTypeIds ??= [];

                declarationFilesByNamespace.TryGetValue(namespaceId, out var declarationFileIds);
                declarationFileIds ??= [];

                var namespaceNode = new NamespaceDeclarationNode(
                    namespaceId,
                    meta.Name,
                    meta.Path,
                    parentNamespaceId == default ? null : parentNamespaceId,
                    childNamespaceIds,
                    containedTypeIds,
                    declarationFileIds);

                if (declarationLocationsByEntityId.TryGetValue(namespaceId, out var declarationLocations))
                {
                    namespaceNode = namespaceNode with
                    {
                        DeclarationLocations = declarationLocations,
                    };
                }

                return namespaceNode;
            })
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.Name,
                    x.Name,
                    x.Path,
                    x.Id.Value),
                StringComparer.Ordinal)
            .ToArray();

        var types = typeIds
            .Select(typeId =>
            {
                var meta = metadataById.TryGetValue(typeId, out var typeMeta)
                    ? typeMeta
                    : new EntityMetadata(typeId);

                namespaceByTypeId.TryGetValue(typeId, out var namespaceId);
                declarationFilesByType.TryGetValue(typeId, out var declarationFileIds);
                declarationFileIds ??= [];
                declaringTypeByType.TryGetValue(typeId, out var declaringTypeId);
                directBasesByType.TryGetValue(typeId, out var directBaseIds);
                directBaseIds ??= [];
                directInterfacesByType.TryGetValue(typeId, out var directInterfaceIds);
                directInterfaceIds ??= [];
                memberIdsByType.TryGetValue(typeId, out var memberIds);
                memberIds ??= [];
                methodIdsByType.TryGetValue(typeId, out var methodIds);
                methodIds ??= [];
                cboDeclarationByTypeId.TryGetValue(typeId, out var cboDeclaration);
                cboMethodBodyByTypeId.TryGetValue(typeId, out var cboMethodBody);
                cboTotalByTypeId.TryGetValue(typeId, out var cboTotal);
                var hasCbo = cboDeclarationByTypeId.ContainsKey(typeId)
                    || cboMethodBodyByTypeId.ContainsKey(typeId)
                    || cboTotalByTypeId.ContainsKey(typeId);

                var analyzableMethodMetricSnapshots = methodIds
                    .Select(methodId =>
                    {
                        methodCoverageByMethodId.TryGetValue(methodId, out var coverage);
                        cyclomaticByMethodId.TryGetValue(methodId, out var cyclomatic);
                        cognitiveByMethodId.TryGetValue(methodId, out var cognitive);
                        halsteadByMethodId.TryGetValue(methodId, out var halstead);
                        maintainabilityByMethodId.TryGetValue(methodId, out var maintainability);

                        return new
                        {
                            CoverageStatus = coverage ?? string.Empty,
                            Cyclomatic = cyclomaticByMethodId.ContainsKey(methodId) ? cyclomatic : (int?)null,
                            Cognitive = cognitiveByMethodId.ContainsKey(methodId) ? cognitive : (int?)null,
                            Halstead = halsteadByMethodId.ContainsKey(methodId) ? halstead : (double?)null,
                            Maintainability = maintainabilityByMethodId.ContainsKey(methodId) ? maintainability : (double?)null,
                        };
                    })
                    .Where(snapshot =>
                        snapshot.CoverageStatus.Equals("analyzable", StringComparison.OrdinalIgnoreCase)
                        && snapshot.Cyclomatic.HasValue
                        && snapshot.Cognitive.HasValue
                        && snapshot.Halstead.HasValue
                        && snapshot.Maintainability.HasValue)
                    .ToArray();

                var methodMetricCount = analyzableMethodMetricSnapshots.Length;
                var averageCyclomatic = methodMetricCount == 0
                    ? 0d
                    : analyzableMethodMetricSnapshots.Average(snapshot => snapshot.Cyclomatic!.Value);
                var averageCognitive = methodMetricCount == 0
                    ? 0d
                    : analyzableMethodMetricSnapshots.Average(snapshot => snapshot.Cognitive!.Value);
                var averageHalstead = methodMetricCount == 0
                    ? 0d
                    : analyzableMethodMetricSnapshots.Average(snapshot => snapshot.Halstead!.Value);
                var averageMaintainability = methodMetricCount == 0
                    ? 0d
                    : analyzableMethodMetricSnapshots.Average(snapshot => snapshot.Maintainability!.Value);

                var typeNode = new TypeDeclarationNode(
                    typeId,
                    ParseTypeKind(meta.TypeKind),
                    meta.Name,
                    meta.Name,
                    meta.Path,
                    namespaceId == default ? null : namespaceId,
                    declaringTypeId == default ? null : declaringTypeId,
                    meta.IsPartialType,
                    declaringTypeId != default,
                    ParseAccessibility(meta.Accessibility),
                    meta.Arity,
                    meta.GenericParameters.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    meta.GenericConstraints.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    directBaseIds.Select(id => new TypeReferenceNode(
                            id,
                            metadataById.TryGetValue(id, out var targetMeta) ? targetMeta.Name : id.Value,
                            GetReferenceResolutionStatus(id, metadataById)))
                        .ToArray(),
                    directInterfaceIds.Select(id => new TypeReferenceNode(
                            id,
                            metadataById.TryGetValue(id, out var targetMeta) ? targetMeta.Name : id.Value,
                            GetReferenceResolutionStatus(id, metadataById)))
                        .ToArray(),
                    memberIds,
                    declarationFileIds);

                var typeMetrics = new TypeMetricNode(
                    HasCbo: hasCbo,
                    CboDeclaration: cboDeclaration,
                    CboMethodBody: cboMethodBody,
                    CboTotal: cboTotal,
                    MethodMetricCount: methodMetricCount,
                    AverageCyclomaticComplexity: averageCyclomatic,
                    AverageCognitiveComplexity: averageCognitive,
                    AverageHalsteadVolume: averageHalstead,
                    AverageMaintainabilityIndex: averageMaintainability);

                if (declarationLocationsByEntityId.TryGetValue(typeId, out var declarationLocations))
                {
                    typeNode = typeNode with
                    {
                        DeclarationLocations = declarationLocations,
                        MethodIds = methodIds,
                        Metrics = typeMetrics,
                    };
                }
                else
                {
                    typeNode = typeNode with
                    {
                        MethodIds = methodIds,
                        Metrics = typeMetrics,
                    };
                }

                return typeNode;
            })
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.NamespaceId is not null && metadataById.TryGetValue(x.NamespaceId.Value, out var namespaceMeta) ? namespaceMeta.Name : string.Empty,
                    x.Name,
                    x.Path,
                    x.Id.Value),
                StringComparer.Ordinal)
            .ToArray();

        var memberIds = containsMemberEdges
            .Select(x => x.Object)
            .Concat(metadataById.Where(x => x.Value.IsType("member-declaration")).Select(x => x.Key))
            .Distinct()
            .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
            .ToArray();

        var members = memberIds
            .Select(memberId =>
            {
                var meta = metadataById.TryGetValue(memberId, out var memberMeta)
                    ? memberMeta
                    : new EntityMetadata(memberId);

                declaringTypeByMember.TryGetValue(memberId, out var declaringTypeId);
                declarationFilesByMember.TryGetValue(memberId, out var declarationFileIds);
                declarationFileIds ??= [];

                declaredTypeByEntity.TryGetValue(memberId, out var declaredTypeId);

                var declaredType = declaredTypeId == default
                    ? (!string.IsNullOrWhiteSpace(meta.DeclaredTypeText)
                        ? new TypeReferenceNode(
                            null,
                            meta.DeclaredTypeText,
                            DeclarationResolutionStatus.SourceTextFallback)
                        : null)
                    : new TypeReferenceNode(
                        declaredTypeId,
                        metadataById.TryGetValue(declaredTypeId, out var declaredTypeMeta)
                            ? declaredTypeMeta.Name
                            : declaredTypeId.Value,
                        GetReferenceResolutionStatus(declaredTypeId, metadataById));

                var memberNode = new MemberDeclarationNode(
                    memberId,
                    ParseMemberKind(meta.MemberKind),
                    meta.Name,
                    meta.Name,
                    declaringTypeId == default ? default : declaringTypeId,
                    ParseAccessibility(meta.Accessibility),
                    declaredType,
                    string.IsNullOrWhiteSpace(meta.ConstantValue) ? null : meta.ConstantValue,
                    declarationFileIds);

                if (declarationLocationsByEntityId.TryGetValue(memberId, out var declarationLocations))
                {
                    memberNode = memberNode with
                    {
                        DeclarationLocations = declarationLocations,
                    };
                }

                return memberNode;
            })
            .Where(x => x.DeclaringTypeId != default)
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.Kind.ToString(),
                    x.Name,
                    x.DeclaredType?.DisplayText ?? string.Empty,
                    x.Id.Value),
                StringComparer.Ordinal)
            .ToArray();

        var methodIds = containsMethodEdges
            .Select(x => x.Object)
            .Concat(metadataById.Where(x => x.Value.IsType("method-declaration")).Select(x => x.Key))
            .Distinct()
            .OrderBy(id => metadataById.TryGetValue(id, out var meta) ? meta.Path : id.Value, StringComparer.Ordinal)
            .ToArray();

        var methods = methodIds
            .Select(methodId =>
            {
                var meta = metadataById.TryGetValue(methodId, out var methodMeta)
                    ? methodMeta
                    : new EntityMetadata(methodId);

                declaringTypeByMethod.TryGetValue(methodId, out var declaringTypeId);
                declarationFilesByMethod.TryGetValue(methodId, out var declarationFileIds);
                declarationFileIds ??= [];
                returnTypeByMethod.TryGetValue(methodId, out var returnTypeId);
                extendedTypeByMethod.TryGetValue(methodId, out var extendedTypeId);
                parameterIdsByMethod.TryGetValue(methodId, out var parameterIds);
                parameterIds ??= [];

                TypeReferenceNode? returnType = null;
                if (returnTypeId != default)
                {
                    returnType = new TypeReferenceNode(
                        returnTypeId,
                        metadataById.TryGetValue(returnTypeId, out var returnTypeMeta)
                            ? returnTypeMeta.Name
                            : returnTypeId.Value,
                        GetReferenceResolutionStatus(returnTypeId, metadataById));
                }
                else if (!string.IsNullOrWhiteSpace(meta.ReturnTypeText))
                {
                    returnType = new TypeReferenceNode(
                        null,
                        meta.ReturnTypeText,
                        DeclarationResolutionStatus.SourceTextFallback);
                }

                TypeReferenceNode? extendedType = null;
                if (extendedTypeId != default)
                {
                    extendedType = new TypeReferenceNode(
                        extendedTypeId,
                        metadataById.TryGetValue(extendedTypeId, out var extendedTypeMeta)
                            ? extendedTypeMeta.Name
                            : extendedTypeId.Value,
                        GetReferenceResolutionStatus(extendedTypeId, metadataById));
                }

                var parameters = parameterIds
                    .Select((parameterId, index) =>
                    {
                        var parameterMeta = metadataById.TryGetValue(parameterId, out var parameterMetadata)
                            ? parameterMetadata
                            : new EntityMetadata(parameterId);

                        declaredTypeByEntity.TryGetValue(parameterId, out var parameterTypeId);
                        TypeReferenceNode? parameterType = null;

                        if (parameterTypeId != default)
                        {
                            parameterType = new TypeReferenceNode(
                                parameterTypeId,
                                metadataById.TryGetValue(parameterTypeId, out var parameterTypeMeta)
                                    ? parameterTypeMeta.Name
                                    : parameterTypeId.Value,
                                GetReferenceResolutionStatus(parameterTypeId, metadataById));
                        }
                        else if (!string.IsNullOrWhiteSpace(parameterMeta.DeclaredTypeText))
                        {
                            parameterType = new TypeReferenceNode(
                                null,
                                parameterMeta.DeclaredTypeText,
                                DeclarationResolutionStatus.SourceTextFallback);
                        }

                        var parameterName = !string.IsNullOrWhiteSpace(parameterMeta.ParameterName)
                            ? parameterMeta.ParameterName
                            : parameterMeta.Name;

                        var ordinal = parameterMeta.ParameterOrdinal >= 0
                            ? parameterMeta.ParameterOrdinal
                            : index;

                        return new MethodParameterNode(parameterName, ordinal, parameterType);
                    })
                    .OrderBy(x => x.Ordinal)
                    .ThenBy(x => x.Name, StringComparer.Ordinal)
                    .ToArray();

                var methodNode = new MethodDeclarationNode(
                    methodId,
                    ParseMethodKind(meta.MethodKind),
                    meta.Name,
                    meta.Name,
                    meta.Path,
                    declaringTypeId == default ? default : declaringTypeId,
                    ParseAccessibility(meta.Accessibility),
                    meta.Arity,
                    parameters,
                    returnType,
                    IsStatic: false,
                    IsAbstract: false,
                    IsVirtual: false,
                    IsOverride: false,
                    IsExtern: false,
                    meta.IsExtensionMethod,
                    extendedType,
                    declarationFileIds);

                var methodMetrics = new MethodMetricNode(
                    CoverageStatus: methodCoverageByMethodId.TryGetValue(methodId, out var coverageStatus)
                        ? coverageStatus
                        : string.Empty,
                    CyclomaticComplexity: cyclomaticByMethodId.TryGetValue(methodId, out var cyclomatic)
                        ? cyclomatic
                        : null,
                    CognitiveComplexity: cognitiveByMethodId.TryGetValue(methodId, out var cognitive)
                        ? cognitive
                        : null,
                    HalsteadVolume: halsteadByMethodId.TryGetValue(methodId, out var halstead)
                        ? halstead
                        : null,
                    MaintainabilityIndex: maintainabilityByMethodId.TryGetValue(methodId, out var maintainability)
                        ? maintainability
                        : null);

                if (declarationLocationsByEntityId.TryGetValue(methodId, out var declarationLocations))
                {
                    methodNode = methodNode with
                    {
                        DeclarationLocations = declarationLocations,
                        Metrics = methodMetrics,
                    };
                }
                else
                {
                    methodNode = methodNode with
                    {
                        Metrics = methodMetrics,
                    };
                }

                return methodNode;
            })
            .Where(x => x.DeclaringTypeId != default)
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.Kind.ToString(),
                    x.Name,
                    x.Signature,
                    x.Id.Value),
                StringComparer.Ordinal)
            .ToArray();

        var methodIdSet = methods
            .Select(x => x.Id)
            .ToHashSet();

        var relations = methodImplementsEdges
            .Select(x => new MethodRelationNode(
                x.Subject,
                MethodRelationKind.ImplementsMethod,
                x.Object,
                null,
                null,
                null,
                GetReferenceResolutionStatus(x.Object, metadataById)))
            .Concat(methodOverridesEdges.Select(x => new MethodRelationNode(
                x.Subject,
                MethodRelationKind.OverridesMethod,
                x.Object,
                null,
                null,
                null,
                GetReferenceResolutionStatus(x.Object, metadataById))))
            .Concat(methodCallsEdges.Select(x =>
            {
                var resolutionStatus = GetReferenceResolutionStatus(x.Object, metadataById);
                if (metadataById.TryGetValue(x.Object, out var targetMeta) && targetMeta.IsType("method-declaration"))
                {
                    return new MethodRelationNode(
                        x.Subject,
                        MethodRelationKind.Calls,
                        x.Object,
                        null,
                        null,
                        null,
                        resolutionStatus,
                        null);
                }

                return new MethodRelationNode(
                    x.Subject,
                    MethodRelationKind.Calls,
                    null,
                    null,
                    new TypeReferenceNode(
                        x.Object,
                        metadataById.TryGetValue(x.Object, out var externalMeta) ? externalMeta.Name : x.Object.Value,
                        resolutionStatus),
                    metadataById.TryGetValue(x.Object, out var callTargetMeta) && !string.IsNullOrWhiteSpace(callTargetMeta.ExternalAssemblyName)
                        ? callTargetMeta.ExternalAssemblyName
                        : null,
                    resolutionStatus,
                    metadataById.TryGetValue(x.Object, out var unresolvedTargetMeta) && !string.IsNullOrWhiteSpace(unresolvedTargetMeta.ResolutionReason)
                        ? unresolvedTargetMeta.ResolutionReason
                        : null);
            }))
            .Concat(methodReadsPropertyEdges.Select(x => new MethodRelationNode(
                x.Subject,
                MethodRelationKind.ReadsProperty,
                null,
                x.Object,
                null,
                null,
                GetReferenceResolutionStatus(x.Object, metadataById))))
            .Concat(methodWritesPropertyEdges.Select(x => new MethodRelationNode(
                x.Subject,
                MethodRelationKind.WritesProperty,
                null,
                x.Object,
                null,
                null,
                GetReferenceResolutionStatus(x.Object, metadataById))))
            .Concat(methodReadsFieldEdges.Select(x => new MethodRelationNode(
                x.Subject,
                MethodRelationKind.ReadsField,
                null,
                x.Object,
                null,
                null,
                GetReferenceResolutionStatus(x.Object, metadataById))))
            .Concat(methodWritesFieldEdges.Select(x => new MethodRelationNode(
                x.Subject,
                MethodRelationKind.WritesField,
                null,
                x.Object,
                null,
                null,
                GetReferenceResolutionStatus(x.Object, metadataById))))
            .Where(x => methodIdSet.Contains(x.SourceMethodId))
            .Distinct()
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.Kind.ToString(),
                    x.SourceMethodId.Value,
                    x.TargetMethodId?.Value ?? x.TargetMemberId?.Value ?? string.Empty,
                    x.TargetMethodId?.Value ?? x.TargetMemberId?.Value ?? string.Empty),
                StringComparer.Ordinal)
            .ToArray();

        return new DeclarationCatalog(namespaces, types, members)
        {
            Methods = new MethodCatalog(methods, relations),
        };
    }

    private static DeclarationAccessibility ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => DeclarationAccessibility.Public,
            "internal" => DeclarationAccessibility.Internal,
            "protected" => DeclarationAccessibility.Protected,
            "protectedinternal" => DeclarationAccessibility.ProtectedInternal,
            "private" => DeclarationAccessibility.Private,
            "privateprotected" => DeclarationAccessibility.PrivateProtected,
            _ => DeclarationAccessibility.Unknown,
        };
    }

    private static TypeDeclarationKind ParseTypeKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "interface" => TypeDeclarationKind.Interface,
            "class" => TypeDeclarationKind.Class,
            "record" => TypeDeclarationKind.Record,
            "struct" => TypeDeclarationKind.Struct,
            "enum" => TypeDeclarationKind.Enum,
            "delegate" => TypeDeclarationKind.Delegate,
            _ => TypeDeclarationKind.Unknown,
        };
    }

    private static MemberDeclarationKind ParseMemberKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "field" => MemberDeclarationKind.Field,
            "property" => MemberDeclarationKind.Property,
            "enum-member" => MemberDeclarationKind.EnumMember,
            "record-parameter" => MemberDeclarationKind.RecordParameter,
            _ => MemberDeclarationKind.Unknown,
        };
    }

    private static MethodDeclarationKind ParseMethodKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "method" => MethodDeclarationKind.Method,
            "constructor" => MethodDeclarationKind.Constructor,
            _ => MethodDeclarationKind.Unknown,
        };
    }

    private static DeclarationResolutionStatus GetReferenceResolutionStatus(
        EntityId targetId,
        IReadOnlyDictionary<EntityId, EntityMetadata> metadataById)
    {
        if (!metadataById.TryGetValue(targetId, out var targetMeta))
        {
            return DeclarationResolutionStatus.Unresolved;
        }

        if (targetMeta.IsType("type-declaration"))
        {
            return DeclarationResolutionStatus.Resolved;
        }

        if (targetMeta.IsType("method-declaration") || targetMeta.IsType("member-declaration"))
        {
            return DeclarationResolutionStatus.Resolved;
        }

        if (targetMeta.IsType("external-type-stub"))
        {
            return DeclarationResolutionStatus.ExternalStub;
        }

        if (targetMeta.IsType("external-method-stub") || targetMeta.IsType("external-member-stub"))
        {
            return DeclarationResolutionStatus.ExternalStub;
        }

        if (targetMeta.IsType("unresolved-type-reference"))
        {
            return DeclarationResolutionStatus.SourceTextFallback;
        }

        return DeclarationResolutionStatus.Unresolved;
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<DeclarationLocationNode>> BuildDeclarationLocationsByEntityId(
        IReadOnlyList<SemanticTriple> triples)
    {
        return triples
            .Where(x => x.Predicate == CorePredicates.DeclarationSourceLocation)
            .Where(x => x.Subject is EntityNode && x.Object is LiteralNode)
            .Select(x => (EntityId: ((EntityNode)x.Subject).Id, Location: ParseDeclarationLocation(((LiteralNode)x.Object).Value?.ToString() ?? string.Empty)))
            .Where(x => x.Location is not null)
            .GroupBy(x => x.EntityId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<DeclarationLocationNode>)x
                    .Select(v => v.Location!)
                    .Distinct()
                    .OrderBy(v => v.FilePath, StringComparer.Ordinal)
                    .ThenBy(v => v.Line)
                    .ThenBy(v => v.Column)
                    .ToArray());
    }

    private static DeclarationLocationNode? ParseDeclarationLocation(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('|', StringSplitOptions.None);
        if (parts.Length != 3)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        if (!int.TryParse(parts[1], out var line) || line <= 0)
        {
            return null;
        }

        if (!int.TryParse(parts[2], out var column) || column <= 0)
        {
            return null;
        }

        return new DeclarationLocationNode(parts[0], line, column);
    }

    private static Dictionary<EntityId, string> BuildStringLiteralMap(
        IReadOnlyList<SemanticTriple> triples,
        PredicateId predicate)
    {
        return triples
            .Where(triple => triple.Predicate == predicate)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .Select(triple => (
                SubjectId: ((EntityNode)triple.Subject).Id,
                Value: ((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty))
            .GroupBy(item => item.SubjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal).First());
    }

    private static Dictionary<EntityId, int> BuildIntLiteralMap(
        IReadOnlyList<SemanticTriple> triples,
        PredicateId predicate)
    {
        return triples
            .Where(triple => triple.Predicate == predicate)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .Select(triple =>
            {
                var subjectId = ((EntityNode)triple.Subject).Id;
                var literalValue = ((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty;
                return (SubjectId: subjectId, Value: int.TryParse(literalValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null);
            })
            .Where(item => item.Value.HasValue)
            .GroupBy(item => item.SubjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value!.Value).OrderBy(x => x).First());
    }

    private static Dictionary<EntityId, double> BuildDoubleLiteralMap(
        IReadOnlyList<SemanticTriple> triples,
        PredicateId predicate)
    {
        return triples
            .Where(triple => triple.Predicate == predicate)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .Select(triple =>
            {
                var subjectId = ((EntityNode)triple.Subject).Id;
                var literalValue = ((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty;
                return (SubjectId: subjectId, Value: double.TryParse(literalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null);
            })
            .Where(item => item.Value.HasValue)
            .GroupBy(item => item.SubjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value!.Value).OrderBy(x => x).First());
    }

    private static IReadOnlyList<EntityEdge> BuildEntityEdges(
        IReadOnlyList<SemanticTriple> triples,
        PredicateId predicate)
    {
        return triples
            .Where(x => x.Predicate == predicate)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => new EntityEdge(((EntityNode)x.Subject).Id, ((EntityNode)x.Object).Id))
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
            TypeKind = string.Empty;
            Accessibility = string.Empty;
            Arity = 0;
            IsPartialType = false;
            GenericParameters = new HashSet<string>(StringComparer.Ordinal);
            GenericConstraints = new HashSet<string>(StringComparer.Ordinal);
            MemberKind = string.Empty;
            MethodKind = string.Empty;
            DeclaredTypeText = string.Empty;
            ReturnTypeText = string.Empty;
            ConstantValue = string.Empty;
            IsExtensionMethod = false;
            ParameterName = string.Empty;
            ParameterOrdinal = -1;
            ExternalAssemblyName = string.Empty;
            ResolutionReason = string.Empty;
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

        public string TypeKind { get; set; }

        public string Accessibility { get; set; }

        public int Arity { get; set; }

        public bool IsPartialType { get; set; }

        public HashSet<string> GenericParameters { get; }

        public HashSet<string> GenericConstraints { get; }

        public string MemberKind { get; set; }

        public string MethodKind { get; set; }

        public string DeclaredTypeText { get; set; }

        public string ReturnTypeText { get; set; }

        public string ConstantValue { get; set; }

        public bool IsExtensionMethod { get; set; }

        public string ParameterName { get; set; }

        public int ParameterOrdinal { get; set; }

        public string ExternalAssemblyName { get; set; }

        public string ResolutionReason { get; set; }

        public bool IsType(string entityType) => EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ContainsEdge(EntityId Subject, EntityId Object);
    private sealed record PackageReferenceEdge(EntityId ProjectId, EntityId PackageId);
    private sealed record ProjectPackageReferenceEdge(EntityId ProjectId, EntityId PackageReferenceId);
    private sealed record PackageReferenceTargetEdge(EntityId PackageReferenceId, EntityId PackageId);
    private sealed record FileHistoryEdge(EntityId FileId, EntityId EventId);
    private sealed record SubmoduleEdge(EntityId RepositoryId, EntityId SubmoduleId);
    private sealed record EntityEdge(EntityId Subject, EntityId Object);

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
