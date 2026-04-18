using System.Xml.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
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

        var headBranch = ResolveHeadBranch(fullRepositoryPath, diagnostics);
        var mainlineBranch = ResolveMainlineBranch(fullRepositoryPath, headBranch, diagnostics);

        triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.HeadBranch, new LiteralNode(headBranch)));
        triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.MainlineBranch, new LiteralNode(mainlineBranch)));

        if (IsWorkingTreeDirty(fullRepositoryPath, diagnostics))
        {
            diagnostics.Add(new IngestionDiagnostic(
                "repository:dirty-working-tree",
                "Repository contains uncommitted changes; snapshot still reflects HEAD."));
        }

        var submodules = DiscoverSubmodules(fullRepositoryPath, diagnostics);
        foreach (var submodule in submodules.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            var submoduleId = _stableIdGenerator.Create(new EntityKey("submodule", submodule.Path));
            AddEntityTriples(triples, submoduleId, "submodule", submodule.Name, submodule.Path);

            if (!string.IsNullOrWhiteSpace(submodule.Url))
            {
                triples.Add(new SemanticTriple(new EntityNode(submoduleId), CorePredicates.SubmoduleUrl, new LiteralNode(submodule.Url)));
            }

            triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.HasSubmodule, new EntityNode(submoduleId)));
        }

        var excludedDirectories = BuildExcludedSubmoduleDirectories(fullRepositoryPath, submodules);
        var solutions = DiscoverSolutions(fullRepositoryPath, excludedDirectories, diagnostics);
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
        foreach (var project in EnumerateFilesExcludingDirectories(fullRepositoryPath, "*.csproj", excludedDirectories))
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
            foreach (var framework in discovery.TargetFrameworks.OrderBy(x => x, StringComparer.Ordinal))
            {
                triples.Add(new SemanticTriple(new EntityNode(projectId), CorePredicates.TargetFramework, new LiteralNode(framework)));
            }

            var resolvedByPackage = DiscoverResolvedPackages(projectPath, diagnostics);

            foreach (var package in discovery.DeclaredPackages.OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                var packageKey = package.PackageId.ToLowerInvariant();
                var packageId = _stableIdGenerator.Create(new EntityKey("package", packageKey));
                AddEntityTriples(triples, packageId, "package", package.PackageId, package.PackageId);
                triples.Add(new SemanticTriple(new EntityNode(projectId), CorePredicates.ReferencesPackage, new EntityNode(packageId)));

                var packageReferenceId = _stableIdGenerator.Create(new EntityKey("package-reference", $"{relativeProjectPath}:{packageKey}"));
                AddEntityTriples(
                    triples,
                    packageReferenceId,
                    "package-reference",
                    package.PackageId,
                    $"{relativeProjectPath}::{package.PackageId}");
                triples.Add(new SemanticTriple(new EntityNode(projectId), CorePredicates.HasPackageReference, new EntityNode(packageReferenceId)));
                triples.Add(new SemanticTriple(new EntityNode(packageReferenceId), CorePredicates.ReferencesPackage, new EntityNode(packageId)));

                if (!string.IsNullOrWhiteSpace(package.DeclaredVersion))
                {
                    triples.Add(new SemanticTriple(new EntityNode(packageReferenceId), CorePredicates.HasDeclaredVersion, new LiteralNode(package.DeclaredVersion)));
                    triples.Add(new SemanticTriple(new EntityNode(packageId), CorePredicates.HasDeclaredVersion, new LiteralNode(package.DeclaredVersion)));
                }

                if (resolvedByPackage.TryGetValue(package.PackageId, out var resolvedVersion) && !string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    triples.Add(new SemanticTriple(new EntityNode(packageReferenceId), CorePredicates.HasResolvedVersion, new LiteralNode(resolvedVersion)));
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
        var fileIdByRelativePath = new Dictionary<string, EntityId>(StringComparer.OrdinalIgnoreCase);
        var dotNetSourceFiles = new List<string>();

        foreach (var relativeFilePath in gitTrackedFiles.OrderBy(x => x, StringComparer.Ordinal))
        {
            var fileId = _stableIdGenerator.Create(new EntityKey("file", relativeFilePath));
            fileIdByRelativePath[relativeFilePath] = fileId;
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

            if (relativeFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                dotNetSourceFiles.Add(relativeFilePath);
            }

            var fileHistory = DiscoverFileHistory(fullRepositoryPath, relativeFilePath, mainlineBranch, diagnostics);
            triples.Add(new SemanticTriple(new EntityNode(fileId), CorePredicates.EditCount, new LiteralNode(fileHistory.Commits.Count.ToString())));

            var lastChange = fileHistory.Commits.FirstOrDefault();
            if (lastChange is not null)
            {
                triples.Add(new SemanticTriple(new EntityNode(fileId), CorePredicates.LastChangeCommitSha, new LiteralNode(lastChange.CommitSha)));
                triples.Add(new SemanticTriple(new EntityNode(fileId), CorePredicates.LastChangedAtUtc, new LiteralNode(lastChange.TimestampUtc)));
                triples.Add(new SemanticTriple(new EntityNode(fileId), CorePredicates.LastChangedBy, new LiteralNode(lastChange.AuthorName)));
            }

            var mergeByCommitSha = fileHistory.MergeToMainlineEvents.ToDictionary(x => x.MergeCommitSha, x => x, StringComparer.Ordinal);

            foreach (var commit in fileHistory.Commits)
            {
                var eventId = _stableIdGenerator.Create(new EntityKey("file-history-event", $"{relativeFilePath}:{commit.CommitSha}"));
                AddEntityTriples(
                    triples,
                    eventId,
                    "file-history-event",
                    commit.CommitSha,
                    relativeFilePath);

                triples.Add(new SemanticTriple(new EntityNode(eventId), CorePredicates.CommitSha, new LiteralNode(commit.CommitSha)));
                triples.Add(new SemanticTriple(new EntityNode(eventId), CorePredicates.CommittedAtUtc, new LiteralNode(commit.TimestampUtc)));
                triples.Add(new SemanticTriple(new EntityNode(eventId), CorePredicates.AuthorName, new LiteralNode(commit.AuthorName)));
                triples.Add(new SemanticTriple(new EntityNode(eventId), CorePredicates.AuthorEmail, new LiteralNode(commit.AuthorEmail)));
                triples.Add(new SemanticTriple(new EntityNode(fileId), CorePredicates.HasHistoryEvent, new EntityNode(eventId)));

                if (mergeByCommitSha.TryGetValue(commit.CommitSha, out var merge))
                {
                    triples.Add(new SemanticTriple(new EntityNode(eventId), CorePredicates.IsMergeToMainline, new LiteralNode("true")));
                    triples.Add(new SemanticTriple(new EntityNode(eventId), CorePredicates.TargetBranch, new LiteralNode(merge.TargetBranch)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(eventId),
                        CorePredicates.SourceBranchFileCommitCount,
                        new LiteralNode(merge.SourceBranchFileCommitCount.ToString())));
                }
            }

            triples.Add(new SemanticTriple(new EntityNode(repositoryId), CorePredicates.Contains, new EntityNode(fileId)));
        }

        IngestNamespaces(
            repositoryId,
            fullRepositoryPath,
            dotNetSourceFiles,
            fileIdByRelativePath,
            triples,
            diagnostics,
            cancellationToken);

        return Task.FromResult(new ProjectStructureAnalysisResult(repositoryId, triples, diagnostics));
    }

    private void IngestNamespaces(
        EntityId repositoryId,
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, EntityId> fileIdByRelativePath,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        NamespaceDiscoveryResult discovery;
        try
        {
            discovery = CSharpDeclarationScanner.Discover(repositoryRoot, sourceFiles, cancellationToken);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "namespace:discovery:failed",
                $"Failed to discover namespaces from source files: {ex.Message}"));
            return;
        }

        if (discovery.Namespaces.Count == 0)
        {
            return;
        }

        var namespaceIdByName = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        foreach (var ns in discovery.Namespaces.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var namespaceId = _stableIdGenerator.Create(new EntityKey("namespace", ns.Name));
            namespaceIdByName[ns.Name] = namespaceId;

            AddEntityTriples(
                triples,
                namespaceId,
                "namespace",
                ns.Name,
                ns.Name.Replace('.', '/'));
        }

        foreach (var ns in discovery.Namespaces.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var namespaceId = namespaceIdByName[ns.Name];

            if (!string.IsNullOrWhiteSpace(ns.ParentName) && namespaceIdByName.TryGetValue(ns.ParentName, out var parentNamespaceId))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(parentNamespaceId),
                    CorePredicates.ContainsNamespace,
                    new EntityNode(namespaceId)));
            }
            else
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(repositoryId),
                    CorePredicates.ContainsNamespace,
                    new EntityNode(namespaceId)));
            }

            foreach (var relativePath in ns.DeclarationFilePaths.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!fileIdByRelativePath.TryGetValue(relativePath, out var fileId))
                {
                    continue;
                }

                triples.Add(new SemanticTriple(
                    new EntityNode(fileId),
                    CorePredicates.DeclaresNamespace,
                    new EntityNode(namespaceId)));
            }
        }

        var typeGroups = discovery.Types
            .GroupBy(x => x.QualifiedName, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();

        var typeIdByQualifiedName = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        var declaredTypeNamesBySimpleName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var pendingMemberTypeLinks = new List<PendingMemberTypeLink>();
        var externalStubIdByReference = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        var unresolvedReferenceIdByText = new Dictionary<string, EntityId>(StringComparer.Ordinal);

        foreach (var group in typeGroups)
        {
            var representative = group
                .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                .First();

            if (!namespaceIdByName.TryGetValue(representative.NamespaceName, out var namespaceId))
            {
                continue;
            }

            var typeId = _stableIdGenerator.Create(new EntityKey("type-declaration", representative.QualifiedName));
            typeIdByQualifiedName[representative.QualifiedName] = typeId;

            if (!declaredTypeNamesBySimpleName.TryGetValue(representative.TypeName, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                declaredTypeNamesBySimpleName[representative.TypeName] = names;
            }

            names.Add(representative.QualifiedName);

            AddEntityTriples(
                triples,
                typeId,
                "type-declaration",
                representative.TypeName,
                representative.QualifiedName);

            var isPartialType = group.Count() > 1 || group.Any(x => x.IsPartialDeclaration);

            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.TypeKind,
                new LiteralNode(representative.Kind)));
            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.Accessibility,
                new LiteralNode(representative.Accessibility)));
            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.IsPartialType,
                new LiteralNode(isPartialType.ToString().ToLowerInvariant())));
            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.Arity,
                new LiteralNode(representative.Arity.ToString())));

            foreach (var genericParameter in group
                         .SelectMany(x => x.GenericParameters)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(typeId),
                    CorePredicates.GenericParameter,
                    new LiteralNode(genericParameter)));
            }

            foreach (var genericConstraint in group
                         .SelectMany(x => x.GenericConstraints)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(typeId),
                    CorePredicates.GenericConstraint,
                    new LiteralNode(genericConstraint)));
            }

            triples.Add(new SemanticTriple(
                new EntityNode(namespaceId),
                CorePredicates.ContainsType,
                new EntityNode(typeId)));

            foreach (var relativeFilePath in group.Select(x => x.RelativeFilePath).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!fileIdByRelativePath.TryGetValue(relativeFilePath, out var fileId))
                {
                    continue;
                }

                triples.Add(new SemanticTriple(
                    new EntityNode(fileId),
                    CorePredicates.DeclaresType,
                    new EntityNode(typeId)));
            }

            var memberGroups = group
                .SelectMany(x => x.Members)
                .GroupBy(x => $"{x.Kind}:{x.Name}", StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

            foreach (var memberGroup in memberGroups)
            {
                var representativeMember = memberGroup
                    .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                    .First();

                var memberNaturalKey = $"{representative.QualifiedName}:{representativeMember.Kind}:{representativeMember.Name}";
                var memberId = _stableIdGenerator.Create(new EntityKey("member-declaration", memberNaturalKey));

                AddEntityTriples(
                    triples,
                    memberId,
                    "member-declaration",
                    representativeMember.Name,
                    $"{representative.QualifiedName}.{representativeMember.Name}");

                triples.Add(new SemanticTriple(
                    new EntityNode(memberId),
                    CorePredicates.MemberKind,
                    new LiteralNode(representativeMember.Kind)));

                triples.Add(new SemanticTriple(
                    new EntityNode(memberId),
                    CorePredicates.Accessibility,
                    new LiteralNode(representativeMember.Accessibility)));

                triples.Add(new SemanticTriple(
                    new EntityNode(typeId),
                    CorePredicates.ContainsMember,
                    new EntityNode(memberId)));

                if (!string.IsNullOrWhiteSpace(representativeMember.DeclaredTypeName))
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(memberId),
                        CorePredicates.HasDeclaredTypeText,
                        new LiteralNode(representativeMember.DeclaredTypeName)));

                    pendingMemberTypeLinks.Add(new PendingMemberTypeLink(
                        memberId,
                        representative.NamespaceName,
                        representativeMember.DeclaredTypeName!,
                        representative.ImportedNamespaces,
                        representative.ImportedAliases));
                }

                if (!string.IsNullOrWhiteSpace(representativeMember.ConstantValue))
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(memberId),
                        CorePredicates.ConstantValue,
                        new LiteralNode(representativeMember.ConstantValue!)));
                }

                foreach (var relativeFilePath in memberGroup
                             .Select(x => x.RelativeFilePath)
                             .Distinct(StringComparer.Ordinal)
                             .OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (!fileIdByRelativePath.TryGetValue(relativeFilePath, out var memberFileId))
                    {
                        continue;
                    }

                    triples.Add(new SemanticTriple(
                        new EntityNode(memberFileId),
                        CorePredicates.DeclaresMember,
                        new EntityNode(memberId)));
                }
            }
        }

        foreach (var group in typeGroups)
        {
            if (!typeIdByQualifiedName.TryGetValue(group.Key, out var sourceTypeId))
            {
                continue;
            }

            var representative = group
                .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                .First();

            if (!string.IsNullOrWhiteSpace(representative.DeclaringTypeQualifiedName) &&
                typeIdByQualifiedName.TryGetValue(representative.DeclaringTypeQualifiedName, out var declaringTypeId))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceTypeId),
                    CorePredicates.HasDeclaringType,
                    new EntityNode(declaringTypeId)));
            }

            foreach (var baseType in representative.DirectBaseTypeNames.OrderBy(x => x, StringComparer.Ordinal))
            {
                var resolvedBaseQualifiedName = ResolveInternalTypeName(
                    representative.NamespaceName,
                    baseType,
                    representative.ImportedNamespaces,
                    representative.ImportedAliases,
                    typeIdByQualifiedName,
                    declaredTypeNamesBySimpleName);
                if (resolvedBaseQualifiedName is null || !typeIdByQualifiedName.TryGetValue(resolvedBaseQualifiedName, out var baseTypeId))
                {
                    var normalizedBaseType = NormalizeTypeReferenceName(baseType);
                    if (IsExternalStubCandidate(normalizedBaseType))
                    {
                        baseTypeId = GetOrCreateExternalStubId(normalizedBaseType, externalStubIdByReference, triples);
                    }
                    else
                    {
                        baseTypeId = GetOrCreateUnresolvedReferenceId(normalizedBaseType, unresolvedReferenceIdByText, triples);
                        diagnostics.Add(new IngestionDiagnostic(
                            "type:resolution:fallback",
                            $"Unresolved base type '{baseType}' for '{representative.QualifiedName}'."));
                    }
                }

                triples.Add(new SemanticTriple(
                    new EntityNode(sourceTypeId),
                    CorePredicates.Inherits,
                    new EntityNode(baseTypeId)));
            }

            foreach (var interfaceType in representative.DirectInterfaceTypeNames.OrderBy(x => x, StringComparer.Ordinal))
            {
                var resolvedInterfaceQualifiedName = ResolveInternalTypeName(
                    representative.NamespaceName,
                    interfaceType,
                    representative.ImportedNamespaces,
                    representative.ImportedAliases,
                    typeIdByQualifiedName,
                    declaredTypeNamesBySimpleName);
                if (resolvedInterfaceQualifiedName is null || !typeIdByQualifiedName.TryGetValue(resolvedInterfaceQualifiedName, out var interfaceTypeId))
                {
                    var normalizedInterfaceType = NormalizeTypeReferenceName(interfaceType);
                    if (IsExternalStubCandidate(normalizedInterfaceType))
                    {
                        interfaceTypeId = GetOrCreateExternalStubId(normalizedInterfaceType, externalStubIdByReference, triples);
                    }
                    else
                    {
                        interfaceTypeId = GetOrCreateUnresolvedReferenceId(normalizedInterfaceType, unresolvedReferenceIdByText, triples);
                        diagnostics.Add(new IngestionDiagnostic(
                            "type:resolution:fallback",
                            $"Unresolved interface type '{interfaceType}' for '{representative.QualifiedName}'."));
                    }
                }

                triples.Add(new SemanticTriple(
                    new EntityNode(sourceTypeId),
                    CorePredicates.Implements,
                    new EntityNode(interfaceTypeId)));
            }
        }

        foreach (var pendingMemberTypeLink in pendingMemberTypeLinks)
        {
            var resolvedMemberTypeName = ResolveInternalTypeName(
                pendingMemberTypeLink.NamespaceName,
                pendingMemberTypeLink.DeclaredTypeName,
                pendingMemberTypeLink.ImportedNamespaces,
                pendingMemberTypeLink.ImportedAliases,
                typeIdByQualifiedName,
                declaredTypeNamesBySimpleName);
            if (resolvedMemberTypeName is null || !typeIdByQualifiedName.TryGetValue(resolvedMemberTypeName, out var resolvedMemberTypeId))
            {
                var normalizedMemberType = NormalizeTypeReferenceName(pendingMemberTypeLink.DeclaredTypeName);
                if (IsExternalStubCandidate(normalizedMemberType))
                {
                    resolvedMemberTypeId = GetOrCreateExternalStubId(normalizedMemberType, externalStubIdByReference, triples);
                }
                else
                {
                    diagnostics.Add(new IngestionDiagnostic(
                        "type:resolution:fallback",
                        $"Unresolved declared member type '{pendingMemberTypeLink.DeclaredTypeName}' for member '{pendingMemberTypeLink.MemberId.Value}'."));
                    continue;
                }
            }

            triples.Add(new SemanticTriple(
                new EntityNode(pendingMemberTypeLink.MemberId),
                CorePredicates.HasDeclaredType,
                new EntityNode(resolvedMemberTypeId)));
        }
    }

    private static string? ResolveInternalTypeName(
        string currentNamespace,
        string referenceName,
        IReadOnlyList<string> importedNamespaces,
        IReadOnlyDictionary<string, string> importedAliases,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<string, HashSet<string>> declaredTypeNamesBySimpleName)
    {
        if (string.IsNullOrWhiteSpace(referenceName))
        {
            return null;
        }

        var normalized = NormalizeTypeReferenceName(referenceName);

        var aliasSplit = normalized.Split('.', 2, StringSplitOptions.TrimEntries);
        if (aliasSplit.Length == 2 && importedAliases.TryGetValue(aliasSplit[0], out var aliasTarget))
        {
            normalized = $"{aliasTarget}.{aliasSplit[1]}";
        }

        if (typeIdByQualifiedName.ContainsKey(normalized))
        {
            return normalized;
        }

        var namespaced = currentNamespace == "<global>"
            ? normalized
            : $"{currentNamespace}.{normalized}";

        if (typeIdByQualifiedName.ContainsKey(namespaced))
        {
            return namespaced;
        }

        foreach (var importedNamespace in importedNamespaces)
        {
            var importedCandidate = $"{importedNamespace}.{normalized}";
            if (typeIdByQualifiedName.ContainsKey(importedCandidate))
            {
                return importedCandidate;
            }
        }

        if (declaredTypeNamesBySimpleName.TryGetValue(normalized, out var candidates) && candidates.Count == 1)
        {
            return candidates.First();
        }

        return null;
    }

    private EntityId GetOrCreateExternalStubId(
        string referenceName,
        Dictionary<string, EntityId> externalStubIdByReference,
        List<SemanticTriple> triples)
    {
        if (externalStubIdByReference.TryGetValue(referenceName, out var existing))
        {
            return existing;
        }

        var stubId = _stableIdGenerator.Create(new EntityKey("external-type-stub", referenceName));
        externalStubIdByReference[referenceName] = stubId;

        AddEntityTriples(
            triples,
            stubId,
            "external-type-stub",
            referenceName,
            $"external/{referenceName}");

        return stubId;
    }

    private EntityId GetOrCreateUnresolvedReferenceId(
        string referenceName,
        Dictionary<string, EntityId> unresolvedReferenceIdByText,
        List<SemanticTriple> triples)
    {
        if (unresolvedReferenceIdByText.TryGetValue(referenceName, out var existing))
        {
            return existing;
        }

        var unresolvedId = _stableIdGenerator.Create(new EntityKey("unresolved-type-reference", referenceName));
        unresolvedReferenceIdByText[referenceName] = unresolvedId;

        AddEntityTriples(
            triples,
            unresolvedId,
            "unresolved-type-reference",
            referenceName,
            $"unresolved/{referenceName}");

        return unresolvedId;
    }

    private static bool IsExternalStubCandidate(string normalizedReferenceName)
    {
        if (string.IsNullOrWhiteSpace(normalizedReferenceName))
        {
            return false;
        }

        return normalizedReferenceName.All(c => char.IsLetterOrDigit(c) || c is '_' or '.');
    }

    private static string NormalizeTypeReferenceName(string referenceName)
    {
        var normalized = referenceName.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized[8..];
        }

        var genericIndex = normalized.IndexOf('<');
        if (genericIndex >= 0)
        {
            normalized = normalized[..genericIndex];
        }

        return normalized.Trim();
    }

    private sealed record PendingMemberTypeLink(
        EntityId MemberId,
        string NamespaceName,
        string DeclaredTypeName,
        IReadOnlyList<string> ImportedNamespaces,
        IReadOnlyDictionary<string, string> ImportedAliases);

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

    private static IReadOnlyList<string> DiscoverSolutions(
        string repositoryPath,
        HashSet<string> excludedDirectories,
        List<IngestionDiagnostic> diagnostics)
    {
        var solutions = EnumerateFilesExcludingDirectories(repositoryPath, "*.sln", excludedDirectories)
            .Concat(EnumerateFilesExcludingDirectories(repositoryPath, "*.slnx", excludedDirectories))
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

            var targetFrameworks = ReadTargetFrameworks(
                project.GetPropertyValue("TargetFramework"),
                project.GetPropertyValue("TargetFrameworks"));

            return new ProjectDiscoveryResult(name, "msbuild", targetFrameworks, declaredPackages);
        }
        catch (Exception ex)
        {
            var fallbackName = Path.GetFileNameWithoutExtension(projectPath);
            IReadOnlyList<string> fallbackTargetFrameworks = Array.Empty<string>();
            var fallbackPackages = Array.Empty<PackageReferenceInfo>();
            try
            {
                var document = XDocument.Load(projectPath);
                var assemblyName = document.Descendants("AssemblyName").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    fallbackName = assemblyName;
                }

                var fallbackTargetFramework = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
                var fallbackTargetFrameworksValue = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
                fallbackTargetFrameworks = ReadTargetFrameworks(fallbackTargetFramework, fallbackTargetFrameworksValue);

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
            return new ProjectDiscoveryResult(fallbackName, "fallback", fallbackTargetFrameworks, fallbackPackages);
        }
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(string? targetFramework, string? targetFrameworks)
    {
        var values = new List<string>();

        Add(targetFramework);

        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            foreach (var value in targetFrameworks.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                Add(value);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            values.Add(value.Trim());
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
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static HashSet<string> BuildExcludedSubmoduleDirectories(
        string repositoryRoot,
        IReadOnlyList<SubmoduleInfo> submodules)
    {
        return submodules
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .Select(x => Path.GetFullPath(Path.Combine(repositoryRoot, x.Path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFilesExcludingDirectories(
        string rootDirectory,
        string searchPattern,
        HashSet<string> excludedDirectories)
    {
        var root = Path.GetFullPath(rootDirectory);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var normalizedDirectory = Path.GetFullPath(directory);

            if (IsInExcludedDirectory(normalizedDirectory, excludedDirectories))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(normalizedDirectory, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(normalizedDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var child in childDirectories)
            {
                var normalizedChild = Path.GetFullPath(child);
                if (IsInExcludedDirectory(normalizedChild, excludedDirectories))
                {
                    continue;
                }

                pending.Push(normalizedChild);
            }
        }
    }

    private static bool IsInExcludedDirectory(string candidateDirectory, HashSet<string> excludedDirectories)
    {
        if (excludedDirectories.Count == 0)
        {
            return false;
        }

        foreach (var excluded in excludedDirectories)
        {
            if (candidateDirectory.Equals(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (candidateDirectory.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private static GitFileHistoryResult DiscoverFileHistory(
        string repositoryRoot,
        string relativeFilePath,
        string mainlineBranch,
        List<IngestionDiagnostic> diagnostics)
    {
        var historyResult = RunGitText(
            repositoryRoot,
            "log",
            "--follow",
            "--date=iso-strict",
            "--format=%H%x1f%aI%x1f%an%x1f%ae%x1f%P",
            "--",
            relativeFilePath);

        if (historyResult.ExitCode != 0)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "file:history:failed",
                $"Failed to discover history for '{relativeFilePath}': {historyResult.Error}"));
            return new GitFileHistoryResult([], []);
        }

        var commits = historyResult.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseHistoryLine)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        var mergeCommits = DiscoverMainlineMergeCommits(repositoryRoot, mainlineBranch, relativeFilePath, diagnostics);

        var mergeEvents = new List<GitMergeEvent>();
        foreach (var commit in mergeCommits)
        {
            if (commit.Parents.Count < 2 || string.IsNullOrWhiteSpace(commit.CommitSha))
            {
                continue;
            }

            var firstParent = commit.Parents[0];
            var sourceParent = commit.Parents[1];
            var sourceBranchCommitCount = CountSourceBranchFileCommits(
                repositoryRoot,
                relativeFilePath,
                firstParent,
                sourceParent,
                diagnostics);

            mergeEvents.Add(new GitMergeEvent(
                commit.CommitSha,
                commit.TimestampUtc,
                commit.AuthorName,
                commit.AuthorEmail,
                mainlineBranch,
                sourceBranchCommitCount));
        }

        var orderedCommits = commits.ToList();
        var seen = commits
            .Select(x => x.CommitSha)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var mergeCommit in mergeCommits)
        {
            if (seen.Add(mergeCommit.CommitSha))
            {
                orderedCommits.Add(mergeCommit);
            }
        }

        return new GitFileHistoryResult(orderedCommits, mergeEvents);
    }

    private static IReadOnlyList<GitHistoryCommit> DiscoverMainlineMergeCommits(
        string repositoryRoot,
        string mainlineBranch,
        string relativeFilePath,
        List<IngestionDiagnostic> diagnostics)
    {
        var reference = $"refs/heads/{mainlineBranch}";
        var result = RunGitText(
            repositoryRoot,
            "log",
            "--first-parent",
            "--merges",
            "--date=iso-strict",
            "--format=%H%x1f%aI%x1f%an%x1f%ae%x1f%P",
            reference,
            "--",
            relativeFilePath);

        if (result.ExitCode != 0)
        {
            result = RunGitText(
                repositoryRoot,
                "log",
                "--first-parent",
                "--merges",
                "--date=iso-strict",
                "--format=%H%x1f%aI%x1f%an%x1f%ae%x1f%P",
                mainlineBranch,
                "--",
                relativeFilePath);
        }

        if (result.ExitCode != 0)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "file:merge:history:failed",
                $"Unable to resolve merge history for '{relativeFilePath}' on mainline '{mainlineBranch}': {result.Error}"));
            return [];
        }

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseHistoryLine)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();
    }

    private static int CountSourceBranchFileCommits(
        string repositoryRoot,
        string relativeFilePath,
        string firstParent,
        string sourceParent,
        List<IngestionDiagnostic> diagnostics)
    {
        var mergeBaseResult = RunGitText(repositoryRoot, "merge-base", firstParent, sourceParent);
        if (mergeBaseResult.ExitCode != 0)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "file:merge:source-count:not-available",
                $"Failed to resolve merge base for file '{relativeFilePath}': {mergeBaseResult.Error}"));
            return 0;
        }

        var mergeBase = mergeBaseResult.Output.Trim();
        if (string.IsNullOrWhiteSpace(mergeBase))
        {
            return 0;
        }

        var range = $"{mergeBase}..{sourceParent}";
        var result = RunGitText(
            repositoryRoot,
            "log",
            "--follow",
            "--format=%H",
            range,
            "--",
            relativeFilePath);

        if (result.ExitCode != 0)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "file:merge:source-count:not-available",
                $"Failed to count source branch commits for '{relativeFilePath}': {result.Error}"));
            return 0;
        }

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static string ResolveHeadBranch(string repositoryRoot, List<IngestionDiagnostic> diagnostics)
    {
        var result = RunGitText(repositoryRoot, "rev-parse", "--abbrev-ref", "HEAD");
        if (result.ExitCode == 0)
        {
            var value = result.Output.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        diagnostics.Add(new IngestionDiagnostic("git:head:branch:not-available", "Unable to resolve HEAD branch; using literal 'HEAD'."));
        return "HEAD";
    }

    private static string ResolveMainlineBranch(
        string repositoryRoot,
        string headBranch,
        List<IngestionDiagnostic> diagnostics)
    {
        var originHead = RunGitText(repositoryRoot, "symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        if (originHead.ExitCode == 0)
        {
            var value = originHead.Output.Trim();
            if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("origin/", StringComparison.Ordinal))
            {
                return value["origin/".Length..];
            }
        }

        foreach (var fallback in new[] { "main", "master" })
        {
            var exists = RunGitText(repositoryRoot, "show-ref", "--verify", "--quiet", $"refs/heads/{fallback}");
            if (exists.ExitCode == 0)
            {
                diagnostics.Add(new IngestionDiagnostic(
                    "git:mainline:branch:fallback",
                    $"Mainline branch resolved using fallback '{fallback}'."));
                return fallback;
            }
        }

        diagnostics.Add(new IngestionDiagnostic(
            "git:mainline:branch:fallback",
            $"Mainline branch unresolved; using HEAD branch '{headBranch}'."));
        return headBranch;
    }

    private static bool IsWorkingTreeDirty(string repositoryRoot, List<IngestionDiagnostic> diagnostics)
    {
        var result = RunGitText(repositoryRoot, "status", "--porcelain");
        if (result.ExitCode != 0)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "git:status:failed",
                $"Failed to evaluate working tree status: {result.Error}"));
            return false;
        }

        return !string.IsNullOrWhiteSpace(result.Output);
    }

    private static IReadOnlyList<SubmoduleInfo> DiscoverSubmodules(string repositoryRoot, List<IngestionDiagnostic> diagnostics)
    {
        var gitModulesPath = Path.Combine(repositoryRoot, ".gitmodules");
        if (!File.Exists(gitModulesPath))
        {
            return [];
        }

        try
        {
            var submodules = new List<SubmoduleInfo>();
            string currentName = string.Empty;
            string currentPath = string.Empty;
            string currentUrl = string.Empty;

            static void Flush(List<SubmoduleInfo> target, string name, string path, string url)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                target.Add(new SubmoduleInfo(
                    string.IsNullOrWhiteSpace(name) ? path : name,
                    path.Replace('\\', '/'),
                    url));
            }

            foreach (var raw in File.ReadLines(gitModulesPath))
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith("[submodule", StringComparison.OrdinalIgnoreCase))
                {
                    Flush(submodules, currentName, currentPath, currentUrl);
                    currentName = ParseSubmoduleName(line);
                    currentPath = string.Empty;
                    currentUrl = string.Empty;
                    continue;
                }

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (parts[0].Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    currentPath = parts[1];
                }
                else if (parts[0].Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    currentUrl = parts[1];
                }
            }

            Flush(submodules, currentName, currentPath, currentUrl);
            return submodules
                .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "submodule:parse:failed",
                $"Failed to parse .gitmodules in '{repositoryRoot}': {ex.Message}"));
            return [];
        }
    }

    private static string ParseSubmoduleName(string sectionLine)
    {
        var firstQuote = sectionLine.IndexOf('"');
        var lastQuote = sectionLine.LastIndexOf('"');

        if (firstQuote >= 0 && lastQuote > firstQuote)
        {
            return sectionLine[(firstQuote + 1)..lastQuote];
        }

        return string.Empty;
    }

    private static GitHistoryCommit? ParseHistoryLine(string line)
    {
        var parts = line.Split('\u001f');
        if (parts.Length != 5)
        {
            return null;
        }

        var normalizedTimestamp = NormalizeUtcTimestamp(parts[1]);

        var parents = parts[4]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        return new GitHistoryCommit(
            parts[0],
            normalizedTimestamp,
            parts[2],
            parts[3],
            parents);
    }

    private static string NormalizeUtcTimestamp(string rawTimestamp)
    {
        if (!DateTimeOffset.TryParse(
                rawTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return rawTimestamp;
        }

        return parsed.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
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

    private static GitTextResult RunGitText(string repositoryRoot, params string[] args)
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
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitTextResult(process.ExitCode, output, error);
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

    private sealed record ProjectDiscoveryResult(
        string Name,
        string Method,
        IReadOnlyList<string> TargetFrameworks,
        IReadOnlyList<PackageReferenceInfo> DeclaredPackages);

    private sealed record PackageReferenceInfo(string PackageId, string? DeclaredVersion);

    private sealed record GitCommandResult(int ExitCode, HashSet<string> Entries, string Error);

    private sealed record GitTextResult(int ExitCode, string Output, string Error);

    private sealed record GitHistoryCommit(
        string CommitSha,
        string TimestampUtc,
        string AuthorName,
        string AuthorEmail,
        IReadOnlyList<string> Parents);

    private sealed record GitMergeEvent(
        string MergeCommitSha,
        string TimestampUtc,
        string AuthorName,
        string AuthorEmail,
        string TargetBranch,
        int SourceBranchFileCommitCount);

    private sealed record GitFileHistoryResult(
        IReadOnlyList<GitHistoryCommit> Commits,
        IReadOnlyList<GitMergeEvent> MergeToMainlineEvents);

    private sealed record SubmoduleInfo(string Name, string Path, string Url);
}
