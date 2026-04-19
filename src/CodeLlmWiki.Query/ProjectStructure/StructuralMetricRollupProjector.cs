using System.Globalization;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class StructuralMetricRollupProjector : IStructuralMetricRollupProjector
{
    public StructuralMetricRollupCatalog Project(StructuralMetricRollupProjectionRequest request)
    {
        var methodMetricsById = BuildMethodMetricsById(request.Triples);
        var typeMetricsById = BuildTypeMetricsById(request.Triples);
        var methodsById = request.Declarations.Methods.Declarations.ToDictionary(x => x.Id, x => x);
        var typesById = request.Declarations.Types.ToDictionary(x => x.Id, x => x);
        var filesById = request.Files.ToDictionary(x => x.Id, x => x);

        var fileProjectMap = BuildFileProjectMap(request.Files, request.Projects);
        var projectCodeKinds = request.Projects.ToDictionary(
            project => project.Id,
            project => ClassifyProjectCodeKind(project.Name, project.Path));
        var fileCodeKinds = request.Files.ToDictionary(
            file => file.Id,
            file =>
            {
                fileProjectMap.TryGetValue(file.Id, out var projectId);
                return ClassifyFileCodeKind(
                    file.Path,
                    projectId is { } mappedProjectId && projectCodeKinds.TryGetValue(mappedProjectId, out var projectKind)
                        ? projectKind
                        : StructuralMetricCodeKind.Production);
            });

        var methodContexts = BuildMethodContexts(methodsById, typesById, filesById, fileProjectMap, fileCodeKinds, methodMetricsById);
        var typeContexts = BuildTypeContexts(typesById, filesById, fileProjectMap, fileCodeKinds, typeMetricsById);

        var methodsByFileId = methodContexts
            .SelectMany(method => method.DeclarationFileIds.Select(fileId => (fileId, method)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<MethodMetricContext>)x
                    .Select(v => v.method)
                    .DistinctBy(v => v.Id)
                    .ToArray());
        var typesByFileId = typeContexts
            .SelectMany(type => type.DeclarationFileIds.Select(fileId => (fileId, type)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<TypeMetricContext>)x
                    .Select(v => v.type)
                    .DistinctBy(v => v.Id)
                    .ToArray());
        var methodsByProjectId = methodContexts
            .Where(x => x.ProjectId is not null)
            .GroupBy(x => x.ProjectId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<MethodMetricContext>)x
                    .DistinctBy(v => v.Id)
                    .ToArray());
        var typesByProjectId = typeContexts
            .Where(x => x.ProjectId is not null)
            .GroupBy(x => x.ProjectId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<TypeMetricContext>)x
                    .DistinctBy(v => v.Id)
                    .ToArray());
        var methodsByNamespaceId = methodContexts
            .Where(x => x.NamespaceId is not null)
            .GroupBy(x => x.NamespaceId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<MethodMetricContext>)x
                    .DistinctBy(v => v.Id)
                    .ToArray());
        var typesByNamespaceId = typeContexts
            .Where(x => x.NamespaceId is not null)
            .GroupBy(x => x.NamespaceId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<TypeMetricContext>)x
                    .DistinctBy(v => v.Id)
                    .ToArray());

        var namespaceDescendants = BuildNamespaceDescendants(request.Declarations.Namespaces);
        var files = request.Files
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .Select(file =>
            {
                var methods = methodsByFileId.TryGetValue(file.Id, out var fileMethods)
                    ? fileMethods
                    : [];
                var types = typesByFileId.TryGetValue(file.Id, out var fileTypes)
                    ? fileTypes
                    : [];
                var rawRollup = BuildRawRollup(methods, types);
                var included = IsIncludedInRanking(rawRollup, methods, types, request.ScopeFilter);
                var rollup = rawRollup with { IncludedInRanking = included };

                return new FileStructuralMetricRollupNode(
                    file.Id,
                    file.Name,
                    file.Path,
                    fileCodeKinds[file.Id],
                    rollup);
            })
            .ToArray();

        var projects = request.Projects
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .Select(project =>
            {
                var methods = methodsByProjectId.TryGetValue(project.Id, out var projectMethods)
                    ? projectMethods
                    : [];
                var types = typesByProjectId.TryGetValue(project.Id, out var projectTypes)
                    ? projectTypes
                    : [];
                var rawRollup = BuildRawRollup(methods, types);
                var included = IsIncludedInRanking(rawRollup, methods, types, request.ScopeFilter);
                var rollup = rawRollup with { IncludedInRanking = included };

                return new ProjectStructuralMetricRollupNode(
                    project.Id,
                    project.Name,
                    project.Path,
                    projectCodeKinds[project.Id],
                    rollup);
            })
            .ToArray();

        var namespaces = request.Declarations.Namespaces
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .Select(namespaceNode =>
            {
                var directMethods = methodsByNamespaceId.TryGetValue(namespaceNode.Id, out var directMethodSet)
                    ? directMethodSet
                    : [];
                var directTypes = typesByNamespaceId.TryGetValue(namespaceNode.Id, out var directTypeSet)
                    ? directTypeSet
                    : [];
                var directRaw = BuildRawRollup(directMethods, directTypes);
                var directIncluded = IsIncludedInRanking(directRaw, directMethods, directTypes, request.ScopeFilter);
                var direct = directRaw with { IncludedInRanking = directIncluded };

                var recursiveMethodSet = namespaceDescendants[namespaceNode.Id]
                    .SelectMany(descendantId => methodsByNamespaceId.TryGetValue(descendantId, out var recursiveMethods) ? recursiveMethods : [])
                    .DistinctBy(x => x.Id)
                    .ToArray();
                var recursiveTypeSet = namespaceDescendants[namespaceNode.Id]
                    .SelectMany(descendantId => typesByNamespaceId.TryGetValue(descendantId, out var recursiveTypes) ? recursiveTypes : [])
                    .DistinctBy(x => x.Id)
                    .ToArray();
                var recursiveRaw = BuildRawRollup(recursiveMethodSet, recursiveTypeSet);
                var recursiveIncluded = IsIncludedInRanking(recursiveRaw, recursiveMethodSet, recursiveTypeSet, request.ScopeFilter);
                var recursive = recursiveRaw with { IncludedInRanking = recursiveIncluded };

                return new NamespaceStructuralMetricRollupNode(
                    namespaceNode.Id,
                    namespaceNode.Name.Equals("<global>", StringComparison.Ordinal) ? "(global)" : namespaceNode.Name,
                    namespaceNode.Path,
                    namespaceNode.Name.Equals("<global>", StringComparison.Ordinal),
                    namespaceNode.ParentNamespaceId,
                    direct,
                    recursive);
            })
            .ToArray();

        var repositoryRaw = BuildRawRollup(methodContexts, typeContexts);
        var repositoryIncluded = IsIncludedInRanking(repositoryRaw, methodContexts, typeContexts, request.ScopeFilter);
        var repository = new RepositoryStructuralMetricRollupNode(
            request.RepositoryId,
            repositoryRaw with { IncludedInRanking = repositoryIncluded });

        return new StructuralMetricRollupCatalog(repository, projects, namespaces, files, request.ScopeFilter);
    }

    private static bool IsIncludedInRanking(
        StructuralMetricScopeRollup rollup,
        IReadOnlyList<MethodMetricContext> methods,
        IReadOnlyList<TypeMetricContext> types,
        StructuralMetricScopeFilter filter)
    {
        if (filter.ExcludeInsufficientDataFromRanking && rollup.Severity == StructuralMetricSeverity.None)
        {
            return false;
        }

        var eligibleMethods = methods.Count(method => method.IsAnalyzable && filter.IncludesCodeKind(method.CodeKind));
        if (eligibleMethods > 0)
        {
            return true;
        }

        var eligibleTypes = types.Count(type => type.HasCbo && filter.IncludesCodeKind(type.CodeKind));
        return eligibleTypes > 0;
    }

    private static StructuralMetricScopeRollup BuildRawRollup(
        IReadOnlyList<MethodMetricContext> methods,
        IReadOnlyList<TypeMetricContext> types)
    {
        var totalMethods = methods.Count;
        var analyzableMethods = methods.Count(x => x.IsAnalyzable);
        var nonAnalyzableMethods = totalMethods - analyzableMethods;

        var methodMetricCount = methods.Count(x => x.IsAnalyzable && x.CyclomaticComplexity.HasValue);
        var avgCyclomatic = methodMetricCount == 0
            ? 0d
            : methods.Where(x => x.IsAnalyzable && x.CyclomaticComplexity.HasValue).Average(x => x.CyclomaticComplexity!.Value);
        var avgCognitive = methodMetricCount == 0
            ? 0d
            : methods.Where(x => x.IsAnalyzable && x.CognitiveComplexity.HasValue).Average(x => x.CognitiveComplexity!.Value);
        var avgHalsteadVolume = methodMetricCount == 0
            ? 0d
            : methods.Where(x => x.IsAnalyzable && x.HalsteadVolume.HasValue).Average(x => x.HalsteadVolume!.Value);
        var avgMaintainability = methodMetricCount == 0
            ? 0d
            : methods.Where(x => x.IsAnalyzable && x.MaintainabilityIndex.HasValue).Average(x => x.MaintainabilityIndex!.Value);

        var totalTypes = types.Count;
        var typeMetricCount = types.Count(x => x.HasCbo);
        var avgCboDeclaration = typeMetricCount == 0
            ? 0d
            : types.Where(x => x.HasCbo).Average(x => x.CboDeclaration);
        var avgCboMethodBody = typeMetricCount == 0
            ? 0d
            : types.Where(x => x.HasCbo).Average(x => x.CboMethodBody);
        var avgCboTotal = typeMetricCount == 0
            ? 0d
            : types.Where(x => x.HasCbo).Average(x => x.CboTotal);

        var severity = analyzableMethods == 0
            ? StructuralMetricSeverity.None
            : StructuralMetricSeverity.Unknown;

        return new StructuralMetricScopeRollup(
            new StructuralMetricCoverage(
                TotalMethods: totalMethods,
                AnalyzableMethods: analyzableMethods,
                NonAnalyzableMethods: nonAnalyzableMethods,
                TotalTypes: totalTypes,
                TypesWithCbo: typeMetricCount),
            new StructuralMetricStatistics(
                MethodMetricCount: methodMetricCount,
                AverageCyclomaticComplexity: avgCyclomatic,
                AverageCognitiveComplexity: avgCognitive,
                AverageHalsteadVolume: avgHalsteadVolume,
                AverageMaintainabilityIndex: avgMaintainability,
                TypeMetricCount: typeMetricCount,
                AverageCboDeclaration: avgCboDeclaration,
                AverageCboMethodBody: avgCboMethodBody,
                AverageCboTotal: avgCboTotal),
            severity,
            IncludedInRanking: false);
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlySet<EntityId>> BuildNamespaceDescendants(
        IReadOnlyList<NamespaceDeclarationNode> namespaces)
    {
        var childrenByParent = namespaces.ToDictionary(
            x => x.Id,
            x => (IReadOnlyList<EntityId>)x.ChildNamespaceIds
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray());

        var descendantsByNamespace = new Dictionary<EntityId, IReadOnlySet<EntityId>>();
        foreach (var namespaceNode in namespaces.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Id.Value, StringComparer.Ordinal))
        {
            descendantsByNamespace[namespaceNode.Id] = CollectDescendants(namespaceNode.Id, childrenByParent);
        }

        return descendantsByNamespace;
    }

    private static IReadOnlySet<EntityId> CollectDescendants(
        EntityId root,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> childrenByParent)
    {
        var collected = new HashSet<EntityId> { root };
        var pending = new Queue<EntityId>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var next = pending.Dequeue();
            if (!childrenByParent.TryGetValue(next, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!collected.Add(child))
                {
                    continue;
                }

                pending.Enqueue(child);
            }
        }

        return collected;
    }

    private static IReadOnlyDictionary<EntityId, EntityId> BuildFileProjectMap(
        IReadOnlyList<FileNode> files,
        IReadOnlyList<ProjectNode> projects)
    {
        var projectDirectories = projects
            .Select(project => new
            {
                project.Id,
                Directory = NormalizePath(Path.GetDirectoryName(project.Path)?.Replace('\\', '/') ?? string.Empty),
            })
            .OrderByDescending(x => x.Directory.Length)
            .ThenBy(x => x.Directory, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .ToArray();

        var fileProjectMap = new Dictionary<EntityId, EntityId>();
        foreach (var file in files)
        {
            var normalizedPath = NormalizePath(file.Path);
            foreach (var projectDirectory in projectDirectories)
            {
                if (PathMatchesProjectDirectory(normalizedPath, projectDirectory.Directory))
                {
                    fileProjectMap[file.Id] = projectDirectory.Id;
                    break;
                }
            }
        }

        return fileProjectMap;
    }

    private static bool PathMatchesProjectDirectory(string filePath, string projectDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return true;
        }

        if (filePath.Equals(projectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return filePath.StartsWith(projectDirectory + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimStart('.').Trim('/').Replace('\\', '/');
    }

    private static StructuralMetricCodeKind ClassifyProjectCodeKind(string projectName, string projectPath)
    {
        var loweredName = projectName.ToLowerInvariant();
        var loweredPath = projectPath.Replace('\\', '/').ToLowerInvariant();
        var isTest = loweredName.Contains("test", StringComparison.Ordinal)
                     || loweredPath.Contains("/test/", StringComparison.Ordinal)
                     || loweredPath.Contains("/tests/", StringComparison.Ordinal)
                     || loweredPath.Contains(".test", StringComparison.Ordinal)
                     || loweredPath.Contains(".tests", StringComparison.Ordinal);

        return isTest
            ? StructuralMetricCodeKind.Test
            : StructuralMetricCodeKind.Production;
    }

    private static StructuralMetricCodeKind ClassifyFileCodeKind(string filePath, StructuralMetricCodeKind projectCodeKind)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var loweredPath = normalizedPath.ToLowerInvariant();

        var isGenerated = loweredPath.EndsWith(".g.cs", StringComparison.Ordinal)
                          || loweredPath.EndsWith(".g.i.cs", StringComparison.Ordinal)
                          || loweredPath.EndsWith(".generated.cs", StringComparison.Ordinal)
                          || loweredPath.EndsWith(".designer.cs", StringComparison.Ordinal)
                          || loweredPath.Contains("/generated/", StringComparison.Ordinal)
                          || loweredPath.Contains("/obj/", StringComparison.Ordinal);
        if (isGenerated)
        {
            return StructuralMetricCodeKind.Generated;
        }

        var isTestFile = loweredPath.Contains("/test/", StringComparison.Ordinal)
                         || loweredPath.Contains("/tests/", StringComparison.Ordinal)
                         || loweredPath.EndsWith("tests.cs", StringComparison.Ordinal);
        if (isTestFile || projectCodeKind == StructuralMetricCodeKind.Test)
        {
            return StructuralMetricCodeKind.Test;
        }

        return StructuralMetricCodeKind.Production;
    }

    private static IReadOnlyList<MethodMetricContext> BuildMethodContexts(
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodsById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typesById,
        IReadOnlyDictionary<EntityId, FileNode> filesById,
        IReadOnlyDictionary<EntityId, EntityId> fileProjectMap,
        IReadOnlyDictionary<EntityId, StructuralMetricCodeKind> fileCodeKinds,
        IReadOnlyDictionary<EntityId, MethodMetricSnapshot> methodMetricsById)
    {
        var contexts = new List<MethodMetricContext>(methodsById.Count);
        foreach (var method in methodsById.Values)
        {
            if (!typesById.TryGetValue(method.DeclaringTypeId, out var declaringType))
            {
                continue;
            }

            var declarationFileIds = method.DeclarationFileIds
                .Where(filesById.ContainsKey)
                .Distinct()
                .OrderBy(id => filesById[id].Path, StringComparer.Ordinal)
                .ThenBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            if (declarationFileIds.Length == 0)
            {
                continue;
            }

            var primaryFileId = declarationFileIds[0];
            var projectId = fileProjectMap.TryGetValue(primaryFileId, out var mappedProjectId)
                ? mappedProjectId
                : (EntityId?)null;
            var codeKind = fileCodeKinds.TryGetValue(primaryFileId, out var mappedCodeKind)
                ? mappedCodeKind
                : StructuralMetricCodeKind.Production;
            var metrics = methodMetricsById.TryGetValue(method.Id, out var methodMetricSnapshot)
                ? methodMetricSnapshot
                : MethodMetricSnapshot.Empty;

            contexts.Add(new MethodMetricContext(
                method.Id,
                declarationFileIds,
                declaringType.NamespaceId,
                projectId,
                codeKind,
                metrics.CoverageStatus.Equals("analyzable", StringComparison.OrdinalIgnoreCase),
                metrics.CyclomaticComplexity,
                metrics.CognitiveComplexity,
                metrics.HalsteadVolume,
                metrics.MaintainabilityIndex));
        }

        return contexts;
    }

    private static IReadOnlyList<TypeMetricContext> BuildTypeContexts(
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typesById,
        IReadOnlyDictionary<EntityId, FileNode> filesById,
        IReadOnlyDictionary<EntityId, EntityId> fileProjectMap,
        IReadOnlyDictionary<EntityId, StructuralMetricCodeKind> fileCodeKinds,
        IReadOnlyDictionary<EntityId, TypeMetricSnapshot> typeMetricsById)
    {
        var contexts = new List<TypeMetricContext>(typesById.Count);
        foreach (var type in typesById.Values)
        {
            var declarationFileIds = type.DeclarationFileIds
                .Where(filesById.ContainsKey)
                .Distinct()
                .OrderBy(id => filesById[id].Path, StringComparer.Ordinal)
                .ThenBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            if (declarationFileIds.Length == 0)
            {
                continue;
            }

            var primaryFileId = declarationFileIds[0];
            var projectId = fileProjectMap.TryGetValue(primaryFileId, out var mappedProjectId)
                ? mappedProjectId
                : (EntityId?)null;
            var codeKind = fileCodeKinds.TryGetValue(primaryFileId, out var mappedCodeKind)
                ? mappedCodeKind
                : StructuralMetricCodeKind.Production;

            var metrics = typeMetricsById.TryGetValue(type.Id, out var snapshot)
                ? snapshot
                : TypeMetricSnapshot.Empty;
            contexts.Add(new TypeMetricContext(
                type.Id,
                declarationFileIds,
                type.NamespaceId,
                projectId,
                codeKind,
                metrics.HasCbo,
                metrics.CboDeclaration,
                metrics.CboMethodBody,
                metrics.CboTotal));
        }

        return contexts;
    }

    private static IReadOnlyDictionary<EntityId, MethodMetricSnapshot> BuildMethodMetricsById(IReadOnlyList<SemanticTriple> triples)
    {
        var coverageByMethodId = triples
            .Where(triple => triple.Predicate == CorePredicates.MetricCoverageStatus)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .ToDictionary(
                triple => ((EntityNode)triple.Subject).Id,
                triple => (((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty),
                EqualityComparer<EntityId>.Default);
        var cyclomaticByMethodId = BuildIntMetricMap(triples, CorePredicates.CyclomaticComplexity);
        var cognitiveByMethodId = BuildIntMetricMap(triples, CorePredicates.CognitiveComplexity);
        var halsteadByMethodId = BuildDoubleMetricMap(triples, CorePredicates.HalsteadVolume);
        var maintainabilityByMethodId = BuildDoubleMetricMap(triples, CorePredicates.MaintainabilityIndex);

        var methodIds = coverageByMethodId.Keys
            .Concat(cyclomaticByMethodId.Keys)
            .Concat(cognitiveByMethodId.Keys)
            .Concat(halsteadByMethodId.Keys)
            .Concat(maintainabilityByMethodId.Keys)
            .Distinct()
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        return methodIds.ToDictionary(
            methodId => methodId,
            methodId => new MethodMetricSnapshot(
                coverageByMethodId.TryGetValue(methodId, out var coverage) ? coverage : string.Empty,
                cyclomaticByMethodId.TryGetValue(methodId, out var cyclomatic) ? cyclomatic : null,
                cognitiveByMethodId.TryGetValue(methodId, out var cognitive) ? cognitive : null,
                halsteadByMethodId.TryGetValue(methodId, out var halstead) ? halstead : null,
                maintainabilityByMethodId.TryGetValue(methodId, out var maintainability) ? maintainability : null));
    }

    private static IReadOnlyDictionary<EntityId, TypeMetricSnapshot> BuildTypeMetricsById(IReadOnlyList<SemanticTriple> triples)
    {
        var cboDeclaration = BuildIntMetricMap(triples, CorePredicates.CboDeclaration);
        var cboMethodBody = BuildIntMetricMap(triples, CorePredicates.CboMethodBody);
        var cboTotal = BuildIntMetricMap(triples, CorePredicates.CboTotal);

        var typeIds = cboDeclaration.Keys
            .Concat(cboMethodBody.Keys)
            .Concat(cboTotal.Keys)
            .Distinct()
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        return typeIds.ToDictionary(
            typeId => typeId,
            typeId => new TypeMetricSnapshot(
                cboDeclaration.TryGetValue(typeId, out var declarationValue) ? declarationValue : 0,
                cboMethodBody.TryGetValue(typeId, out var methodBodyValue) ? methodBodyValue : 0,
                cboTotal.TryGetValue(typeId, out var totalValue) ? totalValue : 0,
                cboDeclaration.ContainsKey(typeId) || cboMethodBody.ContainsKey(typeId) || cboTotal.ContainsKey(typeId)));
    }

    private static Dictionary<EntityId, int> BuildIntMetricMap(IReadOnlyList<SemanticTriple> triples, PredicateId predicate)
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
            .ToDictionary(group => group.Key, group => group.First().Value!.Value);
    }

    private static Dictionary<EntityId, double> BuildDoubleMetricMap(IReadOnlyList<SemanticTriple> triples, PredicateId predicate)
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
            .ToDictionary(group => group.Key, group => group.First().Value!.Value);
    }

    private sealed record MethodMetricSnapshot(
        string CoverageStatus,
        int? CyclomaticComplexity,
        int? CognitiveComplexity,
        double? HalsteadVolume,
        double? MaintainabilityIndex)
    {
        public static MethodMetricSnapshot Empty { get; } = new(string.Empty, null, null, null, null);
    }

    private sealed record TypeMetricSnapshot(
        int CboDeclaration,
        int CboMethodBody,
        int CboTotal,
        bool HasCbo)
    {
        public static TypeMetricSnapshot Empty { get; } = new(0, 0, 0, false);
    }

    private sealed record MethodMetricContext(
        EntityId Id,
        IReadOnlyList<EntityId> DeclarationFileIds,
        EntityId? NamespaceId,
        EntityId? ProjectId,
        StructuralMetricCodeKind CodeKind,
        bool IsAnalyzable,
        int? CyclomaticComplexity,
        int? CognitiveComplexity,
        double? HalsteadVolume,
        double? MaintainabilityIndex);

    private sealed record TypeMetricContext(
        EntityId Id,
        IReadOnlyList<EntityId> DeclarationFileIds,
        EntityId? NamespaceId,
        EntityId? ProjectId,
        StructuralMetricCodeKind CodeKind,
        bool HasCbo,
        int CboDeclaration,
        int CboMethodBody,
        int CboTotal);
}
