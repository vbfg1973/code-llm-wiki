using System.Xml.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.Diagnostics;
using CodeLlmWiki.Ingestion.Telemetry;
using CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;
using CodeLlmWiki.Query.ProjectStructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed class ProjectStructureAnalyzer : IProjectStructureAnalyzer
{
    private readonly IStableIdGenerator _stableIdGenerator;
    private readonly EndpointRuleCatalog _endpointRuleCatalog;
    private readonly ICallResolutionDiagnosticClassifier _callResolutionDiagnosticClassifier;
    private readonly IIngestionStageTelemetry _stageTelemetry;
    private readonly IProjectScopedCompilationProvider _projectScopedCompilationProvider;
    private readonly object _externalStubGate = new();
    private readonly object _unresolvedCallTargetGate = new();

    public ProjectStructureAnalyzer(
        IStableIdGenerator stableIdGenerator,
        EndpointRuleCatalog? endpointRuleCatalog = null,
        ICallResolutionDiagnosticClassifier? callResolutionDiagnosticClassifier = null,
        IIngestionStageTelemetry? stageTelemetry = null,
        IProjectScopedCompilationProvider? projectScopedCompilationProvider = null)
    {
        _stableIdGenerator = stableIdGenerator;
        _endpointRuleCatalog = endpointRuleCatalog ?? EndpointRuleCatalog.Default;
        _callResolutionDiagnosticClassifier = callResolutionDiagnosticClassifier ?? new CallResolutionDiagnosticClassifier();
        _stageTelemetry = stageTelemetry ?? NoOpIngestionStageTelemetry.Instance;
        _projectScopedCompilationProvider = projectScopedCompilationProvider ?? new ProjectScopedCompilationProvider();
    }

    public Task<ProjectStructureAnalysisResult> AnalyzeAsync(
        string repositoryPath,
        CancellationToken cancellationToken,
        ProjectStructureAnalysisOptions? options = null)
    {
        var analysisOptions = options ?? ProjectStructureAnalysisOptions.Default;
        var triples = new List<SemanticTriple>();
        var diagnostics = new List<IngestionDiagnostic>();

        var fullRepositoryPath = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(fullRepositoryPath))
        {
            diagnostics.Add(new IngestionDiagnostic("repository:path:not-found", $"Repository path '{fullRepositoryPath}' does not exist."));
            return Task.FromResult(new ProjectStructureAnalysisResult(default, triples, diagnostics));
        }

        using var projectDiscoveryStage = _stageTelemetry.BeginStage(IngestionStageIds.ProjectDiscovery);

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
        var projectAssemblyNameByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in orderedProjects)
        {
            var relativeProjectPath = ToRelativePath(fullRepositoryPath, projectPath);
            var projectId = _stableIdGenerator.Create(new EntityKey("project", relativeProjectPath));
            projectIdByPath[projectPath] = projectId;

            var discovery = DiscoverProjectMetadata(projectPath, diagnostics);
            projectAssemblyNameByPath[projectPath] = discovery.Name;
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

        projectDiscoveryStage.SetCounters(
            new Dictionary<string, long>
            {
                ["solutions"] = solutions.Count,
                ["projects"] = orderedProjects.Length,
                ["git_tracked_files"] = gitTrackedFiles.Count,
                ["dotnet_source_files"] = dotNetSourceFiles.Count,
            });

        IngestNamespaces(
            repositoryId,
            fullRepositoryPath,
            dotNetSourceFiles,
            fileIdByRelativePath,
            projectAssemblyNameByPath,
            triples,
            diagnostics,
            analysisOptions.EffectiveSemanticCallGraphMaxDegreeOfParallelism,
            cancellationToken);

        return Task.FromResult(new ProjectStructureAnalysisResult(repositoryId, triples, diagnostics));
    }

    private void IngestNamespaces(
        EntityId repositoryId,
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, EntityId> fileIdByRelativePath,
        IReadOnlyDictionary<string, string> projectAssemblyNameByPath,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics,
        int semanticCallGraphMaxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        Dictionary<string, string> sourceTextByRelativePath;
        using (var sourceSnapshotStage = _stageTelemetry.BeginStage(IngestionStageIds.SourceSnapshot))
        {
            sourceTextByRelativePath = BuildHeadSourceSnapshot(repositoryRoot, sourceFiles, diagnostics);
            sourceSnapshotStage.SetCounters(
                new Dictionary<string, long>
                {
                    ["source_files"] = sourceFiles.Count,
                    ["snapshotted_files"] = sourceTextByRelativePath.Count,
                });
        }

        NamespaceDiscoveryResult discovery;
        using (var declarationScanStage = _stageTelemetry.BeginStage(IngestionStageIds.DeclarationScan))
        {
            try
            {
                discovery = CSharpDeclarationScanner.Discover(
                    repositoryRoot,
                    sourceFiles,
                    cancellationToken,
                    sourceTextByRelativePath);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new IngestionDiagnostic(
                    "namespace:discovery:failed",
                    $"Failed to discover namespaces from source files: {ex.Message}"));
                return;
            }

            declarationScanStage.SetCounters(
                new Dictionary<string, long>
                {
                    ["namespaces"] = discovery.Namespaces.Count,
                    ["types"] = discovery.Types.Count,
                });
        }

        if (discovery.Namespaces.Count == 0)
        {
            using var endpointExtractionStage = _stageTelemetry.BeginStage(IngestionStageIds.EndpointExtraction);
            var endpointCountBefore = CountPredicateOccurrences(triples, CorePredicates.EndpointKind);

            CaptureMinimalApiEndpoints(
                repositoryId,
                repositoryRoot,
                sourceFiles,
                sourceTextByRelativePath,
                fileIdByRelativePath,
                triples,
                diagnostics);

            var endpointCountAfter = CountPredicateOccurrences(triples, CorePredicates.EndpointKind);
            endpointExtractionStage.SetCounters(
                new Dictionary<string, long>
                {
                    ["source_files"] = sourceFiles.Count,
                    ["endpoint_count"] = endpointCountAfter,
                    ["endpoint_added"] = Math.Max(0, endpointCountAfter - endpointCountBefore),
                });
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

            foreach (var location in ns.DeclarationLocations
                         .Distinct()
                         .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                         .ThenBy(x => x.Line)
                         .ThenBy(x => x.Column))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(namespaceId),
                    CorePredicates.DeclarationSourceLocation,
                    new LiteralNode(FormatDeclarationSourceLocation(location))));
            }
        }

        var typeGroups = discovery.Types
            .GroupBy(x => x.QualifiedName, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();

        var typeIdByQualifiedName = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        var typeQualifiedNameById = new Dictionary<EntityId, string>();
        var namespaceIdByTypeId = new Dictionary<EntityId, EntityId>();
        var declaredTypeNamesBySimpleName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var methodCandidatesByTypeId = new Dictionary<EntityId, List<MethodRelationshipCandidate>>();
        var directBaseTypeIdsByTypeId = new Dictionary<EntityId, List<EntityId>>();
        var directInterfaceTypeIdsByTypeId = new Dictionary<EntityId, List<EntityId>>();
        var pendingMemberTypeLinks = new List<PendingMemberTypeLink>();
        var pendingMethodReturnTypeLinks = new List<PendingMethodReturnTypeLink>();
        var pendingMethodExtendedTypeLinks = new List<PendingMethodExtendedTypeLink>();
        var pendingMethodParameterTypeLinks = new List<PendingMethodParameterTypeLink>();
        var memberIdByDeclarationLocation = new Dictionary<MemberDeclarationLocationKey, EntityId>();
        var methodIdByDeclarationLocation = new Dictionary<MethodDeclarationLocationKey, EntityId>();
        var declaringTypeIdByMethodId = new Dictionary<EntityId, EntityId>();
        var externalStubIdByReference = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        var unresolvedReferenceIdByText = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        var unresolvedCallTargetIdByText = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        var emittedTypeResolutionFallbackDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        var projectAssemblyContexts = BuildProjectAssemblyContexts(repositoryRoot, projectAssemblyNameByPath);

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
            typeQualifiedNameById[typeId] = representative.QualifiedName;
            namespaceIdByTypeId[typeId] = namespaceId;

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

            foreach (var location in group
                         .Select(x => new DeclarationSourceLocation(x.RelativeFilePath, x.SourceLine, x.SourceColumn))
                         .Distinct()
                         .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                         .ThenBy(x => x.Line)
                         .ThenBy(x => x.Column))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(typeId),
                    CorePredicates.DeclarationSourceLocation,
                    new LiteralNode(FormatDeclarationSourceLocation(location))));
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

                foreach (var location in memberGroup
                             .Select(x => new DeclarationSourceLocation(x.RelativeFilePath, x.SourceLine, x.SourceColumn))
                             .Distinct()
                             .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                             .ThenBy(x => x.Line)
                             .ThenBy(x => x.Column))
                {
                    memberIdByDeclarationLocation[new MemberDeclarationLocationKey(location.RelativeFilePath, location.Line, location.Column)] = memberId;
                    triples.Add(new SemanticTriple(
                        new EntityNode(memberId),
                        CorePredicates.DeclarationSourceLocation,
                        new LiteralNode(FormatDeclarationSourceLocation(location))));
                }
            }

            var assemblyName = ResolveAssemblyNameForSourceFile(representative.RelativeFilePath, projectAssemblyContexts);
            var typeSignature = representative.Arity > 0
                ? $"{representative.TypeName}`{representative.Arity}"
                : representative.TypeName;
            var typeNaturalKey = DeclarationIdentityRules.CreateTypeNaturalKey(
                assemblyName,
                representative.NamespaceName,
                typeSignature);

            var methodGroups = group
                .SelectMany(x => x.Methods)
                .GroupBy(CreateMethodGroupKey, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

            foreach (var methodGroup in methodGroups)
            {
                var representativeMethod = methodGroup
                    .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                    .ThenBy(x => x.SourceLine)
                    .ThenBy(x => x.SourceColumn)
                    .First();

                var orderedParameterTypeSignatures = representativeMethod.Parameters
                    .OrderBy(x => x.Ordinal)
                    .Select(x => NormalizeMethodIdentityTypeSignature(x.DeclaredTypeName ?? string.Empty))
                    .ToArray();

                var methodNaturalKey = DeclarationIdentityRules.CreateMethodNaturalKey(
                    assemblyName,
                    typeNaturalKey,
                    representativeMethod.CanonicalName,
                    orderedParameterTypeSignatures,
                    representativeMethod.Arity);

                var methodId = _stableIdGenerator.Create(new EntityKey("method-declaration", methodNaturalKey));
                var methodPath = BuildMethodPath(representative.QualifiedName, representativeMethod);

                AddEntityTriples(
                    triples,
                    methodId,
                    "method-declaration",
                    representativeMethod.Name,
                    methodPath);

                triples.Add(new SemanticTriple(
                    new EntityNode(methodId),
                    CorePredicates.MethodKind,
                    new LiteralNode(representativeMethod.Kind)));
                triples.Add(new SemanticTriple(
                    new EntityNode(methodId),
                    CorePredicates.Accessibility,
                    new LiteralNode(representativeMethod.Accessibility)));
                triples.Add(new SemanticTriple(
                    new EntityNode(methodId),
                    CorePredicates.Arity,
                    new LiteralNode(representativeMethod.Arity.ToString())));
                var hasAnalyzableBody = methodGroup.Any(x => x.HasAnalyzableBody);

                triples.Add(new SemanticTriple(
                    new EntityNode(typeId),
                    CorePredicates.ContainsMethod,
                    new EntityNode(methodId)));
                declaringTypeIdByMethodId[methodId] = typeId;
                triples.Add(new SemanticTriple(
                    new EntityNode(methodId),
                    CorePredicates.MetricCoverageStatus,
                    new LiteralNode(hasAnalyzableBody ? "analyzable" : "no_analyzable_body")));
                if (!hasAnalyzableBody)
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(methodId),
                        CorePredicates.MetricCoverageReason,
                        new LiteralNode("missing_body")));
                }

                if (representativeMethod.IsExtensionMethod)
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(methodId),
                        CorePredicates.IsExtensionMethod,
                        new LiteralNode("true")));

                    if (!string.IsNullOrWhiteSpace(representativeMethod.ExtendedTypeName))
                    {
                        pendingMethodExtendedTypeLinks.Add(new PendingMethodExtendedTypeLink(
                            methodId,
                            representative.NamespaceName,
                            representativeMethod.ExtendedTypeName!,
                            representative.ImportedNamespaces,
                            representative.ImportedAliases));
                    }
                }

                if (!methodCandidatesByTypeId.TryGetValue(typeId, out var methodCandidates))
                {
                    methodCandidates = new List<MethodRelationshipCandidate>();
                    methodCandidatesByTypeId[typeId] = methodCandidates;
                }

                methodCandidates.Add(new MethodRelationshipCandidate(
                    methodId,
                    representativeMethod.Kind,
                    representativeMethod.CanonicalName,
                    representativeMethod.Arity,
                    orderedParameterTypeSignatures,
                    representativeMethod.IsOverride));

                if (!string.IsNullOrWhiteSpace(representativeMethod.ReturnTypeName))
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(methodId),
                        CorePredicates.HasReturnTypeText,
                        new LiteralNode(representativeMethod.ReturnTypeName!)));

                    pendingMethodReturnTypeLinks.Add(new PendingMethodReturnTypeLink(
                        methodId,
                        representative.NamespaceName,
                        representativeMethod.ReturnTypeName!,
                        representative.ImportedNamespaces,
                        representative.ImportedAliases));
                }

                foreach (var relativeFilePath in methodGroup
                             .Select(x => x.RelativeFilePath)
                             .Distinct(StringComparer.Ordinal)
                             .OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (!fileIdByRelativePath.TryGetValue(relativeFilePath, out var methodFileId))
                    {
                        continue;
                    }

                    triples.Add(new SemanticTriple(
                        new EntityNode(methodFileId),
                        CorePredicates.DeclaresMethod,
                        new EntityNode(methodId)));
                }

                foreach (var location in methodGroup
                             .Select(x => new DeclarationSourceLocation(x.RelativeFilePath, x.SourceLine, x.SourceColumn))
                             .Distinct()
                             .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                             .ThenBy(x => x.Line)
                             .ThenBy(x => x.Column))
                {
                    methodIdByDeclarationLocation[new MethodDeclarationLocationKey(location.RelativeFilePath, location.Line, location.Column)] = methodId;
                    triples.Add(new SemanticTriple(
                        new EntityNode(methodId),
                        CorePredicates.DeclarationSourceLocation,
                        new LiteralNode(FormatDeclarationSourceLocation(location))));
                }

                foreach (var parameter in representativeMethod.Parameters.OrderBy(x => x.Ordinal))
                {
                    var parameterNaturalKey = $"{methodNaturalKey}:{parameter.Ordinal}:{parameter.Name}";
                    var parameterId = _stableIdGenerator.Create(new EntityKey("method-parameter", parameterNaturalKey));

                    AddEntityTriples(
                        triples,
                        parameterId,
                        "method-parameter",
                        parameter.Name,
                        $"{methodPath}::parameter:{parameter.Ordinal}:{parameter.Name}");

                    triples.Add(new SemanticTriple(
                        new EntityNode(parameterId),
                        CorePredicates.ParameterOrdinal,
                        new LiteralNode(parameter.Ordinal.ToString())));
                    triples.Add(new SemanticTriple(
                        new EntityNode(parameterId),
                        CorePredicates.ParameterName,
                        new LiteralNode(parameter.Name)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(methodId),
                        CorePredicates.HasMethodParameter,
                        new EntityNode(parameterId)));

                    if (string.IsNullOrWhiteSpace(parameter.DeclaredTypeName))
                    {
                        continue;
                    }

                    triples.Add(new SemanticTriple(
                        new EntityNode(parameterId),
                        CorePredicates.HasDeclaredTypeText,
                        new LiteralNode(parameter.DeclaredTypeName)));

                    pendingMethodParameterTypeLinks.Add(new PendingMethodParameterTypeLink(
                        parameterId,
                        methodId,
                        representative.NamespaceName,
                        parameter.DeclaredTypeName,
                        representative.ImportedNamespaces,
                        representative.ImportedAliases));
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
            var resolvedInternalBaseTypeIds = new List<EntityId>();
            var resolvedInternalInterfaceTypeIds = new List<EntityId>();

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
                EntityId baseTypeId;
                if (resolvedBaseQualifiedName is not null &&
                    typeIdByQualifiedName.TryGetValue(resolvedBaseQualifiedName, out var resolvedInternalBaseTypeId))
                {
                    baseTypeId = resolvedInternalBaseTypeId;
                    resolvedInternalBaseTypeIds.Add(baseTypeId);
                }
                else
                {
                    var normalizedBaseType = NormalizeTypeReferenceName(baseType);
                    if (IsExternalStubCandidate(normalizedBaseType))
                    {
                        baseTypeId = GetOrCreateExternalStubId(normalizedBaseType, externalStubIdByReference, triples);
                    }
                    else
                    {
                        baseTypeId = GetOrCreateUnresolvedReferenceId(
                            normalizedBaseType,
                            "type-resolution-fallback",
                            unresolvedReferenceIdByText,
                            triples);
                        AddDeduplicatedTypeResolutionFallbackDiagnostic(
                            diagnostics,
                            emittedTypeResolutionFallbackDiagnostics,
                            dedupeScope: "base-type",
                            referenceName: normalizedBaseType,
                            message: $"Unresolved base type '{baseType}' for '{representative.QualifiedName}'.");
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
                EntityId interfaceTypeId;
                if (resolvedInterfaceQualifiedName is not null &&
                    typeIdByQualifiedName.TryGetValue(resolvedInterfaceQualifiedName, out var resolvedInternalInterfaceTypeId))
                {
                    interfaceTypeId = resolvedInternalInterfaceTypeId;
                    resolvedInternalInterfaceTypeIds.Add(interfaceTypeId);
                }
                else
                {
                    var normalizedInterfaceType = NormalizeTypeReferenceName(interfaceType);
                    if (IsExternalStubCandidate(normalizedInterfaceType))
                    {
                        interfaceTypeId = GetOrCreateExternalStubId(normalizedInterfaceType, externalStubIdByReference, triples);
                    }
                    else
                    {
                        interfaceTypeId = GetOrCreateUnresolvedReferenceId(
                            normalizedInterfaceType,
                            "type-resolution-fallback",
                            unresolvedReferenceIdByText,
                            triples);
                        AddDeduplicatedTypeResolutionFallbackDiagnostic(
                            diagnostics,
                            emittedTypeResolutionFallbackDiagnostics,
                            dedupeScope: "interface-type",
                            referenceName: normalizedInterfaceType,
                            message: $"Unresolved interface type '{interfaceType}' for '{representative.QualifiedName}'.");
                    }
                }

                triples.Add(new SemanticTriple(
                    new EntityNode(sourceTypeId),
                    CorePredicates.Implements,
                    new EntityNode(interfaceTypeId)));
            }

            if (resolvedInternalBaseTypeIds.Count > 0)
            {
                directBaseTypeIdsByTypeId[sourceTypeId] = resolvedInternalBaseTypeIds;
            }

            if (resolvedInternalInterfaceTypeIds.Count > 0)
            {
                directInterfaceTypeIdsByTypeId[sourceTypeId] = resolvedInternalInterfaceTypeIds;
            }
        }

        foreach (var pair in methodCandidatesByTypeId.OrderBy(x => x.Key.Value, StringComparer.Ordinal))
        {
            var sourceTypeId = pair.Key;
            var sourceMethods = pair.Value;
            var implementedInterfaceTypeIds = GetImplementedInterfaceTypeIds(
                sourceTypeId,
                directBaseTypeIdsByTypeId,
                directInterfaceTypeIdsByTypeId);

            foreach (var sourceMethod in sourceMethods
                         .Where(x => string.Equals(x.Kind, "method", StringComparison.Ordinal))
                         .OrderBy(x => x.CanonicalName, StringComparer.Ordinal)
                         .ThenBy(x => x.MethodId.Value, StringComparer.Ordinal))
            {
                var sourceMethodName = ExtractMethodNameForRelationship(sourceMethod.CanonicalName);
                var sourceMatchKey = CreateMethodRelationshipMatchKey(
                    sourceMethodName,
                    sourceMethod.Arity,
                    sourceMethod.ParameterTypeSignatures);
                var explicitInterfaceQualifier = ExtractExplicitInterfaceQualifier(sourceMethod.CanonicalName);

                var implementTargets = new HashSet<EntityId>();
                foreach (var interfaceTypeId in implementedInterfaceTypeIds)
                {
                    if (explicitInterfaceQualifier is not null &&
                        !InterfaceQualifierMatches(explicitInterfaceQualifier, interfaceTypeId, typeQualifiedNameById))
                    {
                        continue;
                    }

                    if (!methodCandidatesByTypeId.TryGetValue(interfaceTypeId, out var interfaceMethods))
                    {
                        continue;
                    }

                    foreach (var targetMethod in interfaceMethods.Where(x => string.Equals(x.Kind, "method", StringComparison.Ordinal)))
                    {
                        var targetMatchKey = CreateMethodRelationshipMatchKey(
                            ExtractMethodNameForRelationship(targetMethod.CanonicalName),
                            targetMethod.Arity,
                            targetMethod.ParameterTypeSignatures);
                        if (!targetMatchKey.Equals(sourceMatchKey, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!implementTargets.Add(targetMethod.MethodId))
                        {
                            continue;
                        }

                        triples.Add(new SemanticTriple(
                            new EntityNode(sourceMethod.MethodId),
                            CorePredicates.ImplementsMethod,
                            new EntityNode(targetMethod.MethodId)));
                    }
                }

            }
        }

        using (var semanticCallGraphStage = _stageTelemetry.BeginStage(IngestionStageIds.SemanticCallGraph))
        {
            var callEdgeCountBefore = CountPredicateOccurrences(triples, CorePredicates.Calls);
            CaptureMethodCalls(
                repositoryRoot,
                sourceFiles,
                sourceTextByRelativePath,
                projectAssemblyNameByPath,
                typeIdByQualifiedName,
                methodCandidatesByTypeId,
                declaringTypeIdByMethodId,
                memberIdByDeclarationLocation,
                methodIdByDeclarationLocation,
                externalStubIdByReference,
                unresolvedCallTargetIdByText,
                triples,
                diagnostics,
                semanticCallGraphMaxDegreeOfParallelism);
            var callEdgeCountAfter = CountPredicateOccurrences(triples, CorePredicates.Calls);
            semanticCallGraphStage.SetCounters(
                new Dictionary<string, long>
                {
                    ["method_count"] = methodIdByDeclarationLocation.Count,
                    ["call_edges"] = callEdgeCountAfter,
                    ["call_edges_added"] = Math.Max(0, callEdgeCountAfter - callEdgeCountBefore),
                    ["semantic_mdop"] = Math.Max(1, semanticCallGraphMaxDegreeOfParallelism),
                    ["semantic_projects"] = projectAssemblyNameByPath.Count,
                });
        }

        using (var endpointExtractionStage = _stageTelemetry.BeginStage(IngestionStageIds.EndpointExtraction))
        {
            var endpointCountBefore = CountPredicateOccurrences(triples, CorePredicates.EndpointKind);

            CaptureControllerEndpoints(
                repositoryId,
                repositoryRoot,
                sourceFiles,
                sourceTextByRelativePath,
                typeIdByQualifiedName,
                namespaceIdByTypeId,
                methodIdByDeclarationLocation,
                declaringTypeIdByMethodId,
                fileIdByRelativePath,
                triples,
                diagnostics);

            CaptureHandlerAndCliEndpoints(
                repositoryId,
                repositoryRoot,
                sourceFiles,
                sourceTextByRelativePath,
                typeIdByQualifiedName,
                namespaceIdByTypeId,
                methodIdByDeclarationLocation,
                fileIdByRelativePath,
                triples,
                diagnostics);

            CaptureGrpcEndpoints(
                repositoryId,
                repositoryRoot,
                sourceFiles,
                sourceTextByRelativePath,
                typeIdByQualifiedName,
                namespaceIdByTypeId,
                methodIdByDeclarationLocation,
                fileIdByRelativePath,
                triples,
                diagnostics);

            CaptureMinimalApiEndpoints(
                repositoryId,
                repositoryRoot,
                sourceFiles,
                sourceTextByRelativePath,
                fileIdByRelativePath,
                triples,
                diagnostics);

            var endpointCountAfter = CountPredicateOccurrences(triples, CorePredicates.EndpointKind);
            endpointExtractionStage.SetCounters(
                new Dictionary<string, long>
                {
                    ["source_files"] = sourceFiles.Count,
                    ["endpoint_count"] = endpointCountAfter,
                    ["endpoint_added"] = Math.Max(0, endpointCountAfter - endpointCountBefore),
                });
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
                    AddDeduplicatedTypeResolutionFallbackDiagnostic(
                        diagnostics,
                        emittedTypeResolutionFallbackDiagnostics,
                        dedupeScope: "member-type",
                        referenceName: normalizedMemberType,
                        message: $"Unresolved declared member type '{pendingMemberTypeLink.DeclaredTypeName}' for member '{pendingMemberTypeLink.MemberId.Value}'.");
                    continue;
                }
            }

            triples.Add(new SemanticTriple(
                new EntityNode(pendingMemberTypeLink.MemberId),
                CorePredicates.HasDeclaredType,
                new EntityNode(resolvedMemberTypeId)));
        }

        foreach (var pendingReturnTypeLink in pendingMethodReturnTypeLinks)
        {
            var resolvedReturnTypeName = ResolveInternalTypeName(
                pendingReturnTypeLink.NamespaceName,
                pendingReturnTypeLink.ReturnTypeName,
                pendingReturnTypeLink.ImportedNamespaces,
                pendingReturnTypeLink.ImportedAliases,
                typeIdByQualifiedName,
                declaredTypeNamesBySimpleName);

            if (resolvedReturnTypeName is null || !typeIdByQualifiedName.TryGetValue(resolvedReturnTypeName, out var resolvedReturnTypeId))
            {
                var normalizedReturnType = NormalizeTypeReferenceName(pendingReturnTypeLink.ReturnTypeName);
                if (IsExternalStubCandidate(normalizedReturnType))
                {
                    resolvedReturnTypeId = GetOrCreateExternalStubId(normalizedReturnType, externalStubIdByReference, triples);
                }
                else
                {
                    AddDeduplicatedTypeResolutionFallbackDiagnostic(
                        diagnostics,
                        emittedTypeResolutionFallbackDiagnostics,
                        dedupeScope: "return-type",
                        referenceName: normalizedReturnType,
                        message: $"Unresolved return type '{pendingReturnTypeLink.ReturnTypeName}' for method '{pendingReturnTypeLink.MethodId.Value}'.");
                    resolvedReturnTypeId = GetOrCreateUnresolvedReferenceId(
                        NormalizeTypeReferenceName(pendingReturnTypeLink.ReturnTypeName),
                        "type-resolution-fallback",
                        unresolvedReferenceIdByText,
                        triples);
                }
            }

            triples.Add(new SemanticTriple(
                new EntityNode(pendingReturnTypeLink.MethodId),
                CorePredicates.HasReturnType,
                new EntityNode(resolvedReturnTypeId)));
            triples.Add(new SemanticTriple(
                new EntityNode(pendingReturnTypeLink.MethodId),
                CorePredicates.DependsOnTypeDeclaration,
                new EntityNode(resolvedReturnTypeId)));
        }

        foreach (var pendingExtendedTypeLink in pendingMethodExtendedTypeLinks)
        {
            var resolvedExtendedTypeName = ResolveInternalTypeName(
                pendingExtendedTypeLink.NamespaceName,
                pendingExtendedTypeLink.ExtendedTypeName,
                pendingExtendedTypeLink.ImportedNamespaces,
                pendingExtendedTypeLink.ImportedAliases,
                typeIdByQualifiedName,
                declaredTypeNamesBySimpleName);

            if (resolvedExtendedTypeName is null || !typeIdByQualifiedName.TryGetValue(resolvedExtendedTypeName, out var resolvedExtendedTypeId))
            {
                var normalizedExtendedType = NormalizeTypeReferenceName(pendingExtendedTypeLink.ExtendedTypeName);
                if (IsExternalStubCandidate(normalizedExtendedType))
                {
                    resolvedExtendedTypeId = GetOrCreateExternalStubId(normalizedExtendedType, externalStubIdByReference, triples);
                }
                else
                {
                    AddDeduplicatedTypeResolutionFallbackDiagnostic(
                        diagnostics,
                        emittedTypeResolutionFallbackDiagnostics,
                        dedupeScope: "extended-type",
                        referenceName: normalizedExtendedType,
                        message: $"Unresolved extension target type '{pendingExtendedTypeLink.ExtendedTypeName}' for method '{pendingExtendedTypeLink.MethodId.Value}'.");
                    resolvedExtendedTypeId = GetOrCreateUnresolvedReferenceId(
                        NormalizeTypeReferenceName(pendingExtendedTypeLink.ExtendedTypeName),
                        "type-resolution-fallback",
                        unresolvedReferenceIdByText,
                        triples);
                }
            }

            triples.Add(new SemanticTriple(
                new EntityNode(pendingExtendedTypeLink.MethodId),
                CorePredicates.ExtendsType,
                new EntityNode(resolvedExtendedTypeId)));
            triples.Add(new SemanticTriple(
                new EntityNode(pendingExtendedTypeLink.MethodId),
                CorePredicates.DependsOnTypeDeclaration,
                new EntityNode(resolvedExtendedTypeId)));
        }

        foreach (var pendingParameterTypeLink in pendingMethodParameterTypeLinks)
        {
            var resolvedParameterTypeName = ResolveInternalTypeName(
                pendingParameterTypeLink.NamespaceName,
                pendingParameterTypeLink.DeclaredTypeName,
                pendingParameterTypeLink.ImportedNamespaces,
                pendingParameterTypeLink.ImportedAliases,
                typeIdByQualifiedName,
                declaredTypeNamesBySimpleName);

            if (resolvedParameterTypeName is null || !typeIdByQualifiedName.TryGetValue(resolvedParameterTypeName, out var resolvedParameterTypeId))
            {
                var normalizedParameterType = NormalizeTypeReferenceName(pendingParameterTypeLink.DeclaredTypeName);
                if (IsExternalStubCandidate(normalizedParameterType))
                {
                    resolvedParameterTypeId = GetOrCreateExternalStubId(normalizedParameterType, externalStubIdByReference, triples);
                }
                else
                {
                    AddDeduplicatedTypeResolutionFallbackDiagnostic(
                        diagnostics,
                        emittedTypeResolutionFallbackDiagnostics,
                        dedupeScope: "parameter-type",
                        referenceName: normalizedParameterType,
                        message: $"Unresolved parameter type '{pendingParameterTypeLink.DeclaredTypeName}' for parameter '{pendingParameterTypeLink.ParameterId.Value}'.");
                    resolvedParameterTypeId = GetOrCreateUnresolvedReferenceId(
                        NormalizeTypeReferenceName(pendingParameterTypeLink.DeclaredTypeName),
                        "type-resolution-fallback",
                        unresolvedReferenceIdByText,
                        triples);
                }
            }

            triples.Add(new SemanticTriple(
                new EntityNode(pendingParameterTypeLink.ParameterId),
                CorePredicates.HasDeclaredType,
                new EntityNode(resolvedParameterTypeId)));
            triples.Add(new SemanticTriple(
                new EntityNode(pendingParameterTypeLink.MethodId),
                CorePredicates.DependsOnTypeDeclaration,
                new EntityNode(resolvedParameterTypeId)));
        }
    }

    private void CaptureControllerEndpoints(
        EntityId repositoryId,
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, string> sourceTextByRelativePath,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<EntityId, EntityId> namespaceIdByTypeId,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation,
        IReadOnlyDictionary<EntityId, EntityId> declaringTypeIdByMethodId,
        IReadOnlyDictionary<string, EntityId> fileIdByRelativePath,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        var syntaxTrees = new List<SyntaxTree>(sourceFiles.Count);
        foreach (var relativePath in sourceFiles.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!sourceTextByRelativePath.TryGetValue(relativePath, out var sourceText))
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                sourceText = File.ReadAllText(fullPath);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: relativePath);
            syntaxTrees.Add(syntaxTree);
        }

        if (syntaxTrees.Count == 0)
        {
            return;
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "CodeLlmWiki.EndpointDiscovery",
            syntaxTrees: syntaxTrees,
            references: BuildCompilationReferences(diagnostics),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emittedEndpointGroupIds = new HashSet<EntityId>();
        var emittedEndpointIds = new HashSet<EntityId>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var relativePath = syntaxTree.FilePath;
            fileIdByRelativePath.TryGetValue(relativePath, out var declarationFileId);

            foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (classSymbol is null)
                {
                    continue;
                }

                if (!IsControllerType(classDeclaration, classSymbol))
                {
                    continue;
                }

                var qualifiedTypeName = GetTypeQualifiedName(classSymbol);
                if (!typeIdByQualifiedName.TryGetValue(qualifiedTypeName, out var declaringTypeId))
                {
                    continue;
                }

                var controllerName = classDeclaration.Identifier.ValueText;
                var controllerTokenValue = controllerName.EndsWith("Controller", StringComparison.Ordinal)
                    ? controllerName[..^"Controller".Length]
                    : controllerName;

                var routePrefixTemplates = ExtractRouteTemplates(classDeclaration.AttributeLists);
                if (routePrefixTemplates.Count == 0)
                {
                    routePrefixTemplates = [string.Empty];
                }

                var groupContexts = routePrefixTemplates
                    .Select(routePrefixTemplate =>
                    {
                        var normalizedRoutePrefix = NormalizeRouteKey(ExpandRouteTokens(routePrefixTemplate, controllerTokenValue, string.Empty));
                        var endpointGroupKey = $"{declaringTypeId.Value}:controller:{normalizedRoutePrefix}";
                        var endpointGroupId = _stableIdGenerator.Create(new EntityKey("endpoint-group", endpointGroupKey));
                        return new
                        {
                            AuthoredRoutePrefix = routePrefixTemplate,
                            NormalizedRoutePrefix = normalizedRoutePrefix,
                            GroupKey = endpointGroupKey,
                            GroupId = endpointGroupId,
                        };
                    })
                    .GroupBy(x => x.GroupId)
                    .Select(x => x.First())
                    .ToArray();

                foreach (var groupContext in groupContexts)
                {
                    if (!emittedEndpointGroupIds.Add(groupContext.GroupId))
                    {
                        continue;
                    }

                    AddEntityTriples(
                        triples,
                        groupContext.GroupId,
                        "endpoint-group",
                        controllerName,
                        groupContext.GroupKey);
                    triples.Add(new SemanticTriple(
                        new EntityNode(groupContext.GroupId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("controller")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(groupContext.GroupId),
                        CorePredicates.RoutePrefix,
                        new LiteralNode(groupContext.AuthoredRoutePrefix)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(groupContext.GroupId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(groupContext.NormalizedRoutePrefix)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(groupContext.GroupId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpointGroup,
                        new EntityNode(groupContext.GroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpointGroup,
                        new EntityNode(groupContext.GroupId)));

                    if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceId))
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(groupContext.GroupId),
                            CorePredicates.HasNamespace,
                            new EntityNode(namespaceId)));
                    }

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpointGroup,
                            new EntityNode(groupContext.GroupId)));
                    }
                }

                foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
                {
                    var methodId = ResolveSourceMethodId(methodDeclaration.Identifier.GetLocation(), methodIdByDeclarationLocation);
                    if (methodId == default)
                    {
                        continue;
                    }

                    if (!declaringTypeIdByMethodId.TryGetValue(methodId, out var methodDeclaringTypeId)
                        || methodDeclaringTypeId != declaringTypeId)
                    {
                        continue;
                    }

                    foreach (var groupContext in groupContexts)
                    {
                        var endpointCandidates = ExtractControllerEndpointCandidates(
                            methodDeclaration,
                            groupContext.AuthoredRoutePrefix,
                            controllerTokenValue);
                        foreach (var endpointCandidate in endpointCandidates)
                        {
                            var canonicalSignature = $"{endpointCandidate.HttpMethod} {endpointCandidate.NormalizedRouteKey} {methodId.Value}";
                            var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                            if (!emittedEndpointIds.Add(endpointId))
                            {
                                continue;
                            }

                            AddEntityTriples(
                                triples,
                                endpointId,
                                "endpoint",
                                $"{endpointCandidate.HttpMethod} {endpointCandidate.NormalizedRouteKey}",
                                canonicalSignature);
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.EndpointKind,
                                new LiteralNode("controller-action")));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.EndpointFamily,
                                new LiteralNode("controller")));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.EndpointHttpMethod,
                                new LiteralNode(endpointCandidate.HttpMethod)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.AuthoredRouteText,
                                new LiteralNode(endpointCandidate.AuthoredRouteText)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.NormalizedRouteKey,
                                new LiteralNode(endpointCandidate.NormalizedRouteKey)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.EndpointConfidence,
                                new LiteralNode(endpointCandidate.Confidence)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.RuleId,
                                new LiteralNode("aspnetcore.controller.attribute-route")));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.RuleVersion,
                                new LiteralNode("1")));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.RuleSource,
                                new LiteralNode("code-defined")));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.HasEndpointGroup,
                                new EntityNode(groupContext.GroupId)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.HasDeclaringMethod,
                                new EntityNode(methodId)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointId),
                                CorePredicates.HasDeclaringType,
                                new EntityNode(declaringTypeId)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(repositoryId),
                                CorePredicates.ContainsEndpoint,
                                new EntityNode(endpointId)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(declaringTypeId),
                                CorePredicates.ContainsEndpoint,
                                new EntityNode(endpointId)));
                            triples.Add(new SemanticTriple(
                                new EntityNode(groupContext.GroupId),
                                CorePredicates.ContainsEndpoint,
                                new EntityNode(endpointId)));

                            if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceId))
                            {
                                triples.Add(new SemanticTriple(
                                    new EntityNode(endpointId),
                                    CorePredicates.HasNamespace,
                                    new EntityNode(namespaceId)));
                            }

                            if (declarationFileId != default)
                            {
                                triples.Add(new SemanticTriple(
                                    new EntityNode(declarationFileId),
                                    CorePredicates.DeclaresEndpoint,
                                    new EntityNode(endpointId)));
                            }
                        }
                    }
                }
            }
        }
    }

    private void CaptureHandlerAndCliEndpoints(
        EntityId repositoryId,
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, string> sourceTextByRelativePath,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<EntityId, EntityId> namespaceIdByTypeId,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation,
        IReadOnlyDictionary<string, EntityId> fileIdByRelativePath,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        var syntaxTrees = new List<SyntaxTree>(sourceFiles.Count);
        foreach (var relativePath in sourceFiles.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!sourceTextByRelativePath.TryGetValue(relativePath, out var sourceText))
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                sourceText = File.ReadAllText(fullPath);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: relativePath);
            syntaxTrees.Add(syntaxTree);
        }

        if (syntaxTrees.Count == 0)
        {
            return;
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "CodeLlmWiki.HandlerAndCliDiscovery",
            syntaxTrees: syntaxTrees,
            references: BuildCompilationReferences(diagnostics),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emittedEndpointGroupIds = new HashSet<EntityId>();
        var emittedEndpointIds = new HashSet<EntityId>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var relativePath = syntaxTree.FilePath;
            fileIdByRelativePath.TryGetValue(relativePath, out var declarationFileId);

            foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (classSymbol is null)
                {
                    continue;
                }

                var qualifiedTypeName = GetTypeQualifiedName(classSymbol);
                if (!typeIdByQualifiedName.TryGetValue(qualifiedTypeName, out var declaringTypeId))
                {
                    continue;
                }

                var methodIds = classDeclaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Select(methodDeclaration => ResolveSourceMethodId(methodDeclaration.Identifier.GetLocation(), methodIdByDeclarationLocation))
                    .Where(methodId => methodId != default)
                    .Distinct()
                    .ToArray();

                var preferredMethodId = ResolvePreferredHandlerMethodId(classDeclaration, methodIdByDeclarationLocation);
                if (preferredMethodId == default && methodIds.Length > 0)
                {
                    preferredMethodId = methodIds[0];
                }

                var handlerInterfaceMatches = classSymbol.Interfaces
                    .Select(interfaceSymbol =>
                    {
                        var isMatch = _endpointRuleCatalog.TryMatchHandlerInterface(interfaceSymbol.Name, out var rule);
                        return (InterfaceSymbol: interfaceSymbol, Rule: rule, IsMatch: isMatch);
                    })
                    .Where(x => x.IsMatch && x.Rule is not null)
                    .Select(x => (x.InterfaceSymbol, Rule: x.Rule!))
                    .Distinct()
                    .OrderBy(x => x.InterfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                    .ThenBy(x => x.Rule.RuleId, StringComparer.Ordinal)
                    .ToArray();

                foreach (var (handlerInterface, rule) in handlerInterfaceMatches)
                {
                    var interfaceDisplay = handlerInterface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var interfaceKey = SanitizeRouteKeyToken(interfaceDisplay);
                    var handlerTypeKey = SanitizeRouteKeyToken(classSymbol.Name);
                    var routeKey = $"message-handlers/{interfaceKey}/{handlerTypeKey}";
                    var endpointGroupKey = $"{declaringTypeId.Value}:message-handler:{interfaceKey}";
                    var endpointGroupId = _stableIdGenerator.Create(new EntityKey("endpoint-group", endpointGroupKey));

                    if (emittedEndpointGroupIds.Add(endpointGroupId))
                    {
                        AddEntityTriples(
                            triples,
                            endpointGroupId,
                            "endpoint-group",
                            $"{classSymbol.Name} handlers",
                            endpointGroupKey);
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.EndpointFamily,
                            new LiteralNode("message-handler")));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.RoutePrefix,
                            new LiteralNode(interfaceDisplay)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.NormalizedRouteKey,
                            new LiteralNode(routeKey)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.HasDeclaringType,
                            new EntityNode(declaringTypeId)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(repositoryId),
                            CorePredicates.ContainsEndpointGroup,
                            new EntityNode(endpointGroupId)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(declaringTypeId),
                            CorePredicates.ContainsEndpointGroup,
                            new EntityNode(endpointGroupId)));

                        if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForGroup))
                        {
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointGroupId),
                                CorePredicates.HasNamespace,
                                new EntityNode(namespaceIdForGroup)));
                        }

                        if (declarationFileId != default)
                        {
                            triples.Add(new SemanticTriple(
                                new EntityNode(declarationFileId),
                                CorePredicates.DeclaresEndpointGroup,
                                new EntityNode(endpointGroupId)));
                        }
                    }

                    var handlerMethodId = preferredMethodId == default ? (EntityId?)null : preferredMethodId;
                    var canonicalSignature = $"HANDLE {routeKey} {declaringTypeId.Value}:{interfaceDisplay}";
                    var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                    if (!emittedEndpointIds.Add(endpointId))
                    {
                        continue;
                    }

                    AddEntityTriples(
                        triples,
                        endpointId,
                        "endpoint",
                        $"HANDLE {classSymbol.Name}",
                        canonicalSignature);
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointKind,
                        new LiteralNode("interface-handler")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("message-handler")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointHttpMethod,
                        new LiteralNode("HANDLE")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.AuthoredRouteText,
                        new LiteralNode(interfaceDisplay)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(routeKey)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointConfidence,
                        new LiteralNode("high")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleId,
                        new LiteralNode(rule.RuleId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleVersion,
                        new LiteralNode(rule.RuleVersion)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleSource,
                        new LiteralNode(rule.RuleSource)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasEndpointGroup,
                        new EntityNode(endpointGroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));

                    if (handlerMethodId is { } methodId)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasDeclaringMethod,
                            new EntityNode(methodId)));
                    }

                    if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForEndpoint))
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasNamespace,
                            new EntityNode(namespaceIdForEndpoint)));
                    }

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpoint,
                            new EntityNode(endpointId)));
                    }
                }

                var verbAttributes = classDeclaration.AttributeLists
                    .SelectMany(x => x.Attributes)
                    .Select(attribute => TryExtractCommandLineVerb(attribute, semanticModel, out var verb) ? verb : null)
                    .Where(verb => !string.IsNullOrWhiteSpace(verb))
                    .Select(verb => verb!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(verb => verb, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var verb in verbAttributes)
                {
                    var verbKey = SanitizeRouteKeyToken(verb);
                    var routeKey = $"cli/{verbKey}";
                    var endpointGroupKey = $"{declaringTypeId.Value}:cli";
                    var endpointGroupId = _stableIdGenerator.Create(new EntityKey("endpoint-group", endpointGroupKey));

                    if (emittedEndpointGroupIds.Add(endpointGroupId))
                    {
                        AddEntityTriples(
                            triples,
                            endpointGroupId,
                            "endpoint-group",
                            $"{classSymbol.Name} commands",
                            endpointGroupKey);
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.EndpointFamily,
                            new LiteralNode("cli")));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.RoutePrefix,
                            new LiteralNode("cli")));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.NormalizedRouteKey,
                            new LiteralNode("cli")));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.HasDeclaringType,
                            new EntityNode(declaringTypeId)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(repositoryId),
                            CorePredicates.ContainsEndpointGroup,
                            new EntityNode(endpointGroupId)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(declaringTypeId),
                            CorePredicates.ContainsEndpointGroup,
                            new EntityNode(endpointGroupId)));

                        if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForGroup))
                        {
                            triples.Add(new SemanticTriple(
                                new EntityNode(endpointGroupId),
                                CorePredicates.HasNamespace,
                                new EntityNode(namespaceIdForGroup)));
                        }

                        if (declarationFileId != default)
                        {
                            triples.Add(new SemanticTriple(
                                new EntityNode(declarationFileId),
                                CorePredicates.DeclaresEndpointGroup,
                                new EntityNode(endpointGroupId)));
                        }
                    }

                    var canonicalSignature = $"COMMAND {routeKey} {declaringTypeId.Value}";
                    var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                    if (!emittedEndpointIds.Add(endpointId))
                    {
                        continue;
                    }

                    AddEntityTriples(
                        triples,
                        endpointId,
                        "endpoint",
                        $"COMMAND {verb}",
                        canonicalSignature);
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointKind,
                        new LiteralNode("cli-command")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("cli")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointHttpMethod,
                        new LiteralNode("COMMAND")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.AuthoredRouteText,
                        new LiteralNode(verb)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(routeKey)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointConfidence,
                        new LiteralNode("high")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleId,
                        new LiteralNode("cli.commandlineparser.verb-attribute")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleVersion,
                        new LiteralNode("1")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleSource,
                        new LiteralNode("catalog:default")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasEndpointGroup,
                        new EntityNode(endpointGroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));

                    if (preferredMethodId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasDeclaringMethod,
                            new EntityNode(preferredMethodId)));
                    }

                    if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForEndpoint))
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasNamespace,
                            new EntityNode(namespaceIdForEndpoint)));
                    }

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpoint,
                            new EntityNode(endpointId)));
                    }
                }
            }
        }
    }

    private static bool IsControllerType(ClassDeclarationSyntax declaration, INamedTypeSymbol typeSymbol)
    {
        if (declaration.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal))
        {
            return true;
        }

        if (HasAttribute(declaration.AttributeLists, "ApiController"))
        {
            return true;
        }

        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            var baseName = current.Name;
            if (string.Equals(baseName, "ControllerBase", StringComparison.Ordinal)
                || string.Equals(baseName, "Controller", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private bool TryExtractCommandLineVerb(AttributeSyntax attribute, SemanticModel semanticModel, out string? verb)
    {
        verb = null;

        if (ResolveAttributeTypeSymbol(attribute, semanticModel) is { } attributeTypeSymbol)
        {
            var isErrorType = attributeTypeSymbol.TypeKind == TypeKind.Error;
            if (!isErrorType
                && !string.Equals(attributeTypeSymbol.Name, _endpointRuleCatalog.CliVerbAttributeTypeName, StringComparison.Ordinal))
            {
                return false;
            }

            var namespaceName = attributeTypeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!isErrorType
                && !string.Equals(namespaceName, _endpointRuleCatalog.CliVerbAttributeNamespace, StringComparison.Ordinal))
            {
                return false;
            }

            if (!isErrorType)
            {
                var cliVerbFromSymbol = ExtractFirstStringLiteralArgument(attribute);
                if (string.IsNullOrWhiteSpace(cliVerbFromSymbol))
                {
                    return false;
                }

                verb = cliVerbFromSymbol;
                return true;
            }
        }

        if (!string.Equals(ExtractAttributeName(attribute.Name), "Verb", StringComparison.Ordinal))
        {
            return false;
        }

        var compilationUnit = attribute.SyntaxTree.GetRoot() as CompilationUnitSyntax;
        if (compilationUnit is null)
        {
            return false;
        }

        var hasCommandLineUsing = compilationUnit.Usings.Any(usingDirective =>
            usingDirective.Alias is null
            && !usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
            && string.Equals(usingDirective.Name?.ToString(), _endpointRuleCatalog.CliVerbAttributeNamespace, StringComparison.Ordinal));
        if (!hasCommandLineUsing)
        {
            return false;
        }

        var cliVerb = ExtractFirstStringLiteralArgument(attribute);
        if (string.IsNullOrWhiteSpace(cliVerb))
        {
            return false;
        }

        verb = cliVerb;
        return true;
    }

    private static INamedTypeSymbol? ResolveAttributeTypeSymbol(AttributeSyntax attribute, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(attribute);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            return methodSymbol.ContainingType;
        }

        foreach (var candidateSymbol in symbolInfo.CandidateSymbols)
        {
            if (candidateSymbol is IMethodSymbol candidateMethodSymbol)
            {
                return candidateMethodSymbol.ContainingType;
            }
        }

        return semanticModel.GetTypeInfo(attribute).Type as INamedTypeSymbol;
    }

    private static EntityId ResolvePreferredHandlerMethodId(
        ClassDeclarationSyntax classDeclaration,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation)
    {
        foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!string.Equals(methodDeclaration.Identifier.ValueText, "Handle", StringComparison.Ordinal))
            {
                continue;
            }

            var methodId = ResolveSourceMethodId(methodDeclaration.Identifier.GetLocation(), methodIdByDeclarationLocation);
            if (methodId != default)
            {
                return methodId;
            }
        }

        foreach (var preferredName in new[] { "Execute", "Run", "Invoke" })
        {
            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!string.Equals(methodDeclaration.Identifier.ValueText, preferredName, StringComparison.Ordinal))
                {
                    continue;
                }

                var methodId = ResolveSourceMethodId(methodDeclaration.Identifier.GetLocation(), methodIdByDeclarationLocation);
                if (methodId != default)
                {
                    return methodId;
                }
            }
        }

        return default;
    }

    private static string SanitizeRouteKeyToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasDash = false;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                previousWasDash = false;
                continue;
            }

            if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static List<ControllerEndpointCandidate> ExtractControllerEndpointCandidates(
        MethodDeclarationSyntax methodDeclaration,
        string routePrefixTemplate,
        string controllerTokenValue)
    {
        var candidates = new List<ControllerEndpointCandidate>();
        var actionTokenValue = methodDeclaration.Identifier.ValueText;

        foreach (var attribute in methodDeclaration.AttributeLists.SelectMany(x => x.Attributes))
        {
            var attributeName = ExtractAttributeName(attribute.Name);
            if (string.IsNullOrWhiteSpace(attributeName))
            {
                continue;
            }

            if (!TryMapHttpMethod(attributeName, out var httpMethod))
            {
                continue;
            }

            var routeSuffix = ExtractFirstStringLiteralArgument(attribute);
            var authoredRoute = CombineRouteTemplates(routePrefixTemplate, routeSuffix);
            var normalizedRoute = NormalizeRouteKey(ExpandRouteTokens(authoredRoute, controllerTokenValue, actionTokenValue));
            if (string.IsNullOrWhiteSpace(normalizedRoute))
            {
                normalizedRoute = NormalizeRouteKey(ExpandRouteTokens(routePrefixTemplate, controllerTokenValue, actionTokenValue));
            }

            candidates.Add(new ControllerEndpointCandidate(
                HttpMethod: httpMethod,
                AuthoredRouteText: authoredRoute,
                NormalizedRouteKey: normalizedRoute,
                Confidence: "high"));
        }

        return candidates;
    }

    private static bool TryMapHttpMethod(string attributeName, out string httpMethod)
    {
        switch (attributeName)
        {
            case "HttpGet":
                httpMethod = "GET";
                return true;
            case "HttpPost":
                httpMethod = "POST";
                return true;
            case "HttpPut":
                httpMethod = "PUT";
                return true;
            case "HttpDelete":
                httpMethod = "DELETE";
                return true;
            case "HttpPatch":
                httpMethod = "PATCH";
                return true;
            case "HttpHead":
                httpMethod = "HEAD";
                return true;
            case "HttpOptions":
                httpMethod = "OPTIONS";
                return true;
            case "Route":
                httpMethod = "ANY";
                return true;
            default:
                httpMethod = string.Empty;
                return false;
        }
    }

    private static IReadOnlyList<string> ExtractRouteTemplates(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var templates = attributeLists
            .SelectMany(x => x.Attributes)
            .Where(x => string.Equals(ExtractAttributeName(x.Name), "Route", StringComparison.Ordinal))
            .Select(ExtractFirstStringLiteralArgument)
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return templates;
    }

    private static string? ExtractFirstStringLiteralArgument(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (argument.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }

    private static string ExtractAttributeName(NameSyntax attributeNameSyntax)
    {
        var rawName = attributeNameSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => attributeNameSyntax.ToString(),
        };

        return rawName.EndsWith("Attribute", StringComparison.Ordinal)
            ? rawName[..^"Attribute".Length]
            : rawName;
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string expectedName)
    {
        return attributeLists
            .SelectMany(x => x.Attributes)
            .Any(attribute => string.Equals(ExtractAttributeName(attribute.Name), expectedName, StringComparison.Ordinal));
    }

    private static string CombineRouteTemplates(string? routePrefix, string? routeSuffix)
    {
        var prefix = routePrefix?.Trim() ?? string.Empty;
        var suffix = routeSuffix?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return suffix;
        }

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return prefix;
        }

        return $"{prefix.TrimEnd('/')}/{suffix.TrimStart('/')}";
    }

    private static string ExpandRouteTokens(string route, string controllerTokenValue, string actionTokenValue)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        return route
            .Replace("[controller]", controllerTokenValue, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionTokenValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRouteKey(string route)
    {
        var value = route.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Replace('\\', '/');
        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        value = value.Trim('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var segments = value
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

        return string.Join('/', segments).ToLowerInvariant();
    }

    private void CaptureGrpcEndpoints(
        EntityId repositoryId,
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, string> sourceTextByRelativePath,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<EntityId, EntityId> namespaceIdByTypeId,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation,
        IReadOnlyDictionary<string, EntityId> fileIdByRelativePath,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        var syntaxTrees = new List<SyntaxTree>(sourceFiles.Count);
        foreach (var relativePath in sourceFiles.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!sourceTextByRelativePath.TryGetValue(relativePath, out var sourceText))
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                sourceText = File.ReadAllText(fullPath);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: relativePath);
            syntaxTrees.Add(syntaxTree);
        }

        if (syntaxTrees.Count == 0)
        {
            return;
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "CodeLlmWiki.GrpcDiscovery",
            syntaxTrees: syntaxTrees,
            references: BuildCompilationReferences(diagnostics),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emittedEndpointGroupIds = new HashSet<EntityId>();
        var emittedEndpointIds = new HashSet<EntityId>();
        var unresolvedReferenceIdByText = new Dictionary<string, EntityId>(StringComparer.Ordinal);

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var relativePath = syntaxTree.FilePath;
            fileIdByRelativePath.TryGetValue(relativePath, out var declarationFileId);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryExtractMapGrpcService(invocation, out var serviceTypeSyntax))
                {
                    continue;
                }

                var serviceTypeName = NormalizeTypeReferenceName(serviceTypeSyntax.ToString());
                if (string.IsNullOrWhiteSpace(serviceTypeName))
                {
                    serviceTypeName = "UnknownService";
                }

                var serviceTypeSymbol = semanticModel.GetTypeInfo(serviceTypeSyntax).Type as INamedTypeSymbol;
                var serviceTypeQualifiedName = serviceTypeSymbol is null ? null : GetTypeQualifiedName(serviceTypeSymbol);
                var hasResolvedServiceType = false;
                var resolvedServiceTypeId = default(EntityId);
                if (!string.IsNullOrWhiteSpace(serviceTypeQualifiedName)
                    && typeIdByQualifiedName.TryGetValue(serviceTypeQualifiedName!, out var serviceTypeId))
                {
                    hasResolvedServiceType = true;
                    resolvedServiceTypeId = serviceTypeId;
                }

                var declaringTypeId = hasResolvedServiceType
                    ? resolvedServiceTypeId
                    : GetOrCreateUnresolvedReferenceId(
                        serviceTypeName,
                        "grpc-service-type-unresolved",
                        unresolvedReferenceIdByText,
                        triples);
                var serviceName = hasResolvedServiceType
                    ? serviceTypeSymbol!.Name
                    : serviceTypeName;
                var serviceKey = SanitizeRouteKeyToken(serviceName);
                var normalizedRoutePrefix = $"grpc/{serviceKey}";
                var endpointGroupKey = $"{declaringTypeId.Value}:grpc";
                var endpointGroupId = _stableIdGenerator.Create(new EntityKey("endpoint-group", endpointGroupKey));
                if (emittedEndpointGroupIds.Add(endpointGroupId))
                {
                    AddEntityTriples(
                        triples,
                        endpointGroupId,
                        "endpoint-group",
                        $"{serviceName} gRPC service",
                        endpointGroupKey);
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("grpc")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.RoutePrefix,
                        new LiteralNode(normalizedRoutePrefix)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(normalizedRoutePrefix)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpointGroup,
                        new EntityNode(endpointGroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpointGroup,
                        new EntityNode(endpointGroupId)));

                    if (hasResolvedServiceType && namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForGroup))
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId),
                            CorePredicates.HasNamespace,
                            new EntityNode(namespaceIdForGroup)));
                    }

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpointGroup,
                            new EntityNode(endpointGroupId)));
                    }
                }

                if (!hasResolvedServiceType || serviceTypeSymbol is null)
                {
                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    var location = $"{relativePath}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
                    var normalizedRouteKey = $"{normalizedRoutePrefix}/unresolved";
                    var canonicalSignature = $"GRPC {normalizedRouteKey} {location}";
                    var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                    if (!emittedEndpointIds.Add(endpointId))
                    {
                        continue;
                    }

                    AddEntityTriples(
                        triples,
                        endpointId,
                        "endpoint",
                        $"GRPC {serviceName}",
                        canonicalSignature);
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointKind,
                        new LiteralNode("grpc-service-unresolved")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("grpc")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointHttpMethod,
                        new LiteralNode("GRPC")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.AuthoredRouteText,
                        new LiteralNode(serviceName)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(normalizedRouteKey)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointConfidence,
                        new LiteralNode("low")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.ResolutionReason,
                        new LiteralNode("grpc-service-type-unresolved")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleId,
                        new LiteralNode("grpc.aspnetcore.mapgrpcservice")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleVersion,
                        new LiteralNode("1")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleSource,
                        new LiteralNode("code-defined")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasEndpointGroup,
                        new EntityNode(endpointGroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpoint,
                            new EntityNode(endpointId)));
                    }

                    diagnostics.Add(new IngestionDiagnostic(
                        "endpoint:grpc:service-type-unresolved",
                        $"gRPC service type '{serviceName}' could not be resolved for '{relativePath}'."));
                    continue;
                }

                var grpcMethods = serviceTypeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(methodSymbol =>
                        methodSymbol.MethodKind == MethodKind.Ordinary
                        && !methodSymbol.IsStatic
                        && methodSymbol.DeclaredAccessibility == Accessibility.Public)
                    .OrderBy(methodSymbol => methodSymbol.Name, StringComparer.Ordinal)
                    .ToArray();

                if (grpcMethods.Length == 0)
                {
                    var normalizedRouteKey = $"{normalizedRoutePrefix}/unresolved";
                    var canonicalSignature = $"GRPC {normalizedRouteKey} {declaringTypeId.Value}";
                    var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                    if (!emittedEndpointIds.Add(endpointId))
                    {
                        continue;
                    }

                    AddEntityTriples(
                        triples,
                        endpointId,
                        "endpoint",
                        $"GRPC {serviceName}",
                        canonicalSignature);
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointKind,
                        new LiteralNode("grpc-service-partial")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("grpc")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointHttpMethod,
                        new LiteralNode("GRPC")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.AuthoredRouteText,
                        new LiteralNode(serviceName)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(normalizedRouteKey)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointConfidence,
                        new LiteralNode("medium")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.ResolutionReason,
                        new LiteralNode("grpc-service-methods-unresolved")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleId,
                        new LiteralNode("grpc.aspnetcore.mapgrpcservice")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleVersion,
                        new LiteralNode("1")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleSource,
                        new LiteralNode("code-defined")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasEndpointGroup,
                        new EntityNode(endpointGroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));

                    if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForEndpoint))
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasNamespace,
                            new EntityNode(namespaceIdForEndpoint)));
                    }

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpoint,
                            new EntityNode(endpointId)));
                    }

                    diagnostics.Add(new IngestionDiagnostic(
                        "endpoint:grpc:service-methods-unresolved",
                        $"gRPC service '{serviceName}' has no resolvable public methods."));
                    continue;
                }

                foreach (var grpcMethod in grpcMethods)
                {
                    var methodId = grpcMethod.Locations
                        .Where(location => location.IsInSource)
                        .Select(location => ResolveSourceMethodId(location, methodIdByDeclarationLocation))
                        .FirstOrDefault(resolvedMethodId => resolvedMethodId != default);
                    var methodName = grpcMethod.Name;
                    var methodKey = SanitizeRouteKeyToken(methodName);
                    var normalizedRouteKey = $"{normalizedRoutePrefix}/{methodKey}";
                    var canonicalSignature = $"GRPC {normalizedRouteKey} {declaringTypeId.Value}:{methodName}";
                    var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                    if (!emittedEndpointIds.Add(endpointId))
                    {
                        continue;
                    }

                    AddEntityTriples(
                        triples,
                        endpointId,
                        "endpoint",
                        $"GRPC {serviceName}.{methodName}",
                        canonicalSignature);
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointKind,
                        new LiteralNode("grpc-service-method")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointFamily,
                        new LiteralNode("grpc")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointHttpMethod,
                        new LiteralNode("GRPC")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.AuthoredRouteText,
                        new LiteralNode($"{serviceName}/{methodName}")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.NormalizedRouteKey,
                        new LiteralNode(normalizedRouteKey)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.EndpointConfidence,
                        new LiteralNode("high")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleId,
                        new LiteralNode("grpc.aspnetcore.mapgrpcservice")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleVersion,
                        new LiteralNode("1")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.RuleSource,
                        new LiteralNode("code-defined")));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasEndpointGroup,
                        new EntityNode(endpointGroupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasDeclaringType,
                        new EntityNode(declaringTypeId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(repositoryId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(declaringTypeId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointGroupId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));

                    if (methodId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasDeclaringMethod,
                            new EntityNode(methodId)));
                    }
                    else
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.ResolutionReason,
                            new LiteralNode("grpc-service-method-unresolved")));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.EndpointConfidence,
                            new LiteralNode("medium")));
                    }

                    if (namespaceIdByTypeId.TryGetValue(declaringTypeId, out var namespaceIdForEndpoint))
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointId),
                            CorePredicates.HasNamespace,
                            new EntityNode(namespaceIdForEndpoint)));
                    }

                    if (declarationFileId != default)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(declarationFileId),
                            CorePredicates.DeclaresEndpoint,
                            new EntityNode(endpointId)));
                    }
                }
            }
        }
    }

    private static bool TryExtractMapGrpcService(
        InvocationExpressionSyntax invocation,
        out TypeSyntax serviceTypeSyntax)
    {
        serviceTypeSyntax = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (memberAccess.Name is not GenericNameSyntax genericName
            || !string.Equals(genericName.Identifier.ValueText, "MapGrpcService", StringComparison.Ordinal)
            || genericName.TypeArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        serviceTypeSyntax = genericName.TypeArgumentList.Arguments[0];
        return true;
    }

    private void CaptureMinimalApiEndpoints(
        EntityId repositoryId,
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, string> sourceTextByRelativePath,
        IReadOnlyDictionary<string, EntityId> fileIdByRelativePath,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        var syntaxTrees = new List<SyntaxTree>(sourceFiles.Count);
        foreach (var relativePath in sourceFiles.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!sourceTextByRelativePath.TryGetValue(relativePath, out var sourceText))
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                sourceText = File.ReadAllText(fullPath);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: relativePath);
            syntaxTrees.Add(syntaxTree);
        }

        if (syntaxTrees.Count == 0)
        {
            return;
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "CodeLlmWiki.MinimalApiDiscovery",
            syntaxTrees: syntaxTrees,
            references: BuildCompilationReferences(diagnostics),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emittedEndpointGroupIds = new HashSet<EntityId>();
        var emittedEndpointIds = new HashSet<EntityId>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var relativePath = syntaxTree.FilePath;
            fileIdByRelativePath.TryGetValue(relativePath, out var declarationFileId);
            var groupRoutePrefixBySymbol = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

            foreach (var localDeclaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation)
                    {
                        continue;
                    }

                    if (!TryExtractMapGroup(invocation, out var receiverExpression, out var routePrefix))
                    {
                        continue;
                    }

                    var parentPrefix = ResolveMinimalApiRoutePrefix(receiverExpression, groupRoutePrefixBySymbol, semanticModel);
                    var composedPrefix = CombineRouteTemplates(parentPrefix, routePrefix);
                    var variableSymbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                    if (variableSymbol is not null)
                    {
                        groupRoutePrefixBySymbol[variableSymbol] = composedPrefix;
                    }
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryExtractMapEndpoint(invocation, out var receiverExpression, out var httpMethod, out var routeSuffix))
                {
                    continue;
                }

                var routePrefix = ResolveMinimalApiRoutePrefix(receiverExpression, groupRoutePrefixBySymbol, semanticModel);
                var authoredRoute = CombineRouteTemplates(routePrefix, routeSuffix);
                var normalizedRoute = NormalizeRouteKey(authoredRoute);
                if (string.IsNullOrWhiteSpace(normalizedRoute))
                {
                    continue;
                }

                EntityId? endpointGroupId = null;
                var normalizedRoutePrefix = NormalizeRouteKey(routePrefix);
                if (!string.IsNullOrWhiteSpace(normalizedRoutePrefix))
                {
                    var endpointGroupKey = $"{relativePath}:minimal-api:{normalizedRoutePrefix}";
                    endpointGroupId = _stableIdGenerator.Create(new EntityKey("endpoint-group", endpointGroupKey));

                    if (emittedEndpointGroupIds.Add(endpointGroupId.Value))
                    {
                        AddEntityTriples(
                            triples,
                            endpointGroupId.Value,
                            "endpoint-group",
                            $"Minimal API Group {normalizedRoutePrefix}",
                            endpointGroupKey);
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId.Value),
                            CorePredicates.EndpointFamily,
                            new LiteralNode("minimal-api")));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId.Value),
                            CorePredicates.RoutePrefix,
                            new LiteralNode(routePrefix)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(endpointGroupId.Value),
                            CorePredicates.NormalizedRouteKey,
                            new LiteralNode(normalizedRoutePrefix)));
                        triples.Add(new SemanticTriple(
                            new EntityNode(repositoryId),
                            CorePredicates.ContainsEndpointGroup,
                            new EntityNode(endpointGroupId.Value)));

                        if (declarationFileId != default)
                        {
                            triples.Add(new SemanticTriple(
                                new EntityNode(declarationFileId),
                                CorePredicates.DeclaresEndpointGroup,
                                new EntityNode(endpointGroupId.Value)));
                        }
                    }
                }

                var lineSpan = invocation.GetLocation().GetLineSpan();
                var location = $"{relativePath}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
                var canonicalSignature = $"{httpMethod} {normalizedRoute} {location}";
                var endpointId = _stableIdGenerator.Create(new EntityKey("endpoint", canonicalSignature));
                if (!emittedEndpointIds.Add(endpointId))
                {
                    continue;
                }

                AddEntityTriples(
                    triples,
                    endpointId,
                    "endpoint",
                    $"{httpMethod} {normalizedRoute}",
                    canonicalSignature);
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.EndpointKind,
                    new LiteralNode("minimal-api-route")));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.EndpointFamily,
                    new LiteralNode("minimal-api")));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.EndpointHttpMethod,
                    new LiteralNode(httpMethod)));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.AuthoredRouteText,
                    new LiteralNode(authoredRoute)));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.NormalizedRouteKey,
                    new LiteralNode(normalizedRoute)));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.EndpointConfidence,
                    new LiteralNode("high")));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.RuleId,
                    new LiteralNode("aspnetcore.minimalapi.map")));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.RuleVersion,
                    new LiteralNode("1")));
                triples.Add(new SemanticTriple(
                    new EntityNode(endpointId),
                    CorePredicates.RuleSource,
                    new LiteralNode("code-defined")));
                triples.Add(new SemanticTriple(
                    new EntityNode(repositoryId),
                    CorePredicates.ContainsEndpoint,
                    new EntityNode(endpointId)));

                if (endpointGroupId is { } groupId)
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(endpointId),
                        CorePredicates.HasEndpointGroup,
                        new EntityNode(groupId)));
                    triples.Add(new SemanticTriple(
                        new EntityNode(groupId),
                        CorePredicates.ContainsEndpoint,
                        new EntityNode(endpointId)));
                }

                if (declarationFileId != default)
                {
                    triples.Add(new SemanticTriple(
                        new EntityNode(declarationFileId),
                        CorePredicates.DeclaresEndpoint,
                        new EntityNode(endpointId)));
                }
            }
        }
    }

    private static bool TryExtractMapGroup(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax receiverExpression,
        out string routePrefix)
    {
        receiverExpression = null!;
        routePrefix = string.Empty;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (!string.Equals(memberAccess.Name.Identifier.ValueText, "MapGroup", StringComparison.Ordinal))
        {
            return false;
        }

        var routeArgument = ExtractFirstStringLiteralArgument(invocation.ArgumentList);
        if (string.IsNullOrWhiteSpace(routeArgument))
        {
            return false;
        }

        receiverExpression = memberAccess.Expression;
        routePrefix = routeArgument;
        return true;
    }

    private static bool TryExtractMapEndpoint(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax receiverExpression,
        out string httpMethod,
        out string routeSuffix)
    {
        receiverExpression = null!;
        httpMethod = string.Empty;
        routeSuffix = string.Empty;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        switch (memberAccess.Name.Identifier.ValueText)
        {
            case "MapGet":
                httpMethod = "GET";
                break;
            case "MapPost":
                httpMethod = "POST";
                break;
            case "MapPut":
                httpMethod = "PUT";
                break;
            case "MapDelete":
                httpMethod = "DELETE";
                break;
            case "MapPatch":
                httpMethod = "PATCH";
                break;
            default:
                return false;
        }

        var routeArgument = ExtractFirstStringLiteralArgument(invocation.ArgumentList);
        if (string.IsNullOrWhiteSpace(routeArgument))
        {
            return false;
        }

        receiverExpression = memberAccess.Expression;
        routeSuffix = routeArgument;
        return true;
    }

    private static string ResolveMinimalApiRoutePrefix(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> groupRoutePrefixBySymbol,
        SemanticModel semanticModel)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                return symbol is not null && groupRoutePrefixBySymbol.TryGetValue(symbol, out var prefix)
                    ? prefix
                    : string.Empty;
            case InvocationExpressionSyntax invocation when TryExtractMapGroup(invocation, out var parent, out var routePrefix):
                return CombineRouteTemplates(
                    ResolveMinimalApiRoutePrefix(parent, groupRoutePrefixBySymbol, semanticModel),
                    routePrefix);
            default:
                return string.Empty;
        }
    }

    private static string? ExtractFirstStringLiteralArgument(ArgumentListSyntax? argumentList)
    {
        if (argumentList is null || argumentList.Arguments.Count == 0)
        {
            return null;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }

    private void CaptureMethodCalls(
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, string> sourceTextByRelativePath,
        IReadOnlyDictionary<string, string> projectAssemblyNameByPath,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<EntityId, List<MethodRelationshipCandidate>> methodCandidatesByTypeId,
        IReadOnlyDictionary<EntityId, EntityId> declaringTypeIdByMethodId,
        IReadOnlyDictionary<MemberDeclarationLocationKey, EntityId> memberIdByDeclarationLocation,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation,
        Dictionary<string, EntityId> externalStubIdByReference,
        Dictionary<string, EntityId> unresolvedCallTargetIdByText,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics,
        int semanticCallGraphMaxDegreeOfParallelism)
    {
        if (sourceFiles.Count == 0)
        {
            return;
        }

        var references = BuildCompilationReferences(diagnostics);
        var semanticContext = _projectScopedCompilationProvider.Build(
            new ProjectScopedCompilationRequest(
                RepositoryRoot: repositoryRoot,
                SourceFiles: sourceFiles,
                SourceTextByRelativePath: sourceTextByRelativePath,
                ProjectAssemblyNameByPath: projectAssemblyNameByPath,
                ProjectPaths: projectAssemblyNameByPath.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                References: references),
            diagnostics);

        var projectBatches = BuildSemanticCallProjectBatches(repositoryRoot, sourceFiles, projectAssemblyNameByPath);
        var effectiveMdop = Math.Max(1, semanticCallGraphMaxDegreeOfParallelism);
        List<SemanticCallProjectBatchResult> projectResults;

        if (effectiveMdop == 1 || projectBatches.Count <= 1)
        {
            projectResults = projectBatches
                .Select(batch => ProcessSemanticCallProjectBatch(
                    batch,
                    semanticContext,
                    typeIdByQualifiedName,
                    methodCandidatesByTypeId,
                    declaringTypeIdByMethodId,
                    memberIdByDeclarationLocation,
                    methodIdByDeclarationLocation,
                    externalStubIdByReference,
                    unresolvedCallTargetIdByText))
                .ToList();
        }
        else
        {
            var bag = new ConcurrentBag<SemanticCallProjectBatchResult>();
            Parallel.ForEach(
                projectBatches,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = effectiveMdop,
                },
                batch =>
                {
                    var result = ProcessSemanticCallProjectBatch(
                        batch,
                        semanticContext,
                        typeIdByQualifiedName,
                        methodCandidatesByTypeId,
                        declaringTypeIdByMethodId,
                        memberIdByDeclarationLocation,
                        methodIdByDeclarationLocation,
                        externalStubIdByReference,
                        unresolvedCallTargetIdByText);
                    bag.Add(result);
                });
            projectResults = bag.ToList();
        }

        var mergedDeclarationDependenciesByTypeId = new Dictionary<EntityId, HashSet<EntityId>>();
        var mergedMethodBodyDependenciesByTypeId = new Dictionary<EntityId, HashSet<EntityId>>();

        foreach (var result in projectResults)
        {
            MergeTypeDependencyMap(mergedDeclarationDependenciesByTypeId, result.DeclarationDependenciesByTypeId);
            MergeTypeDependencyMap(mergedMethodBodyDependenciesByTypeId, result.MethodBodyDependenciesByTypeId);
        }

        var orderedTriples = projectResults
            .SelectMany(x => x.Triples)
            .OrderBy(CreateDeterministicTripleSortKey, StringComparer.Ordinal)
            .ToArray();
        triples.AddRange(orderedTriples);

        var orderedDiagnostics = projectResults
            .SelectMany(x => x.Diagnostics)
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Message, StringComparer.Ordinal)
            .ToArray();
        diagnostics.AddRange(orderedDiagnostics);

        EmitCboMetricsByType(typeIdByQualifiedName.Values, mergedDeclarationDependenciesByTypeId, mergedMethodBodyDependenciesByTypeId, triples);
    }

    private SemanticCallProjectBatchResult ProcessSemanticCallProjectBatch(
        SemanticCallProjectBatch projectBatch,
        IProjectScopedSemanticContext semanticContext,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<EntityId, List<MethodRelationshipCandidate>> methodCandidatesByTypeId,
        IReadOnlyDictionary<EntityId, EntityId> declaringTypeIdByMethodId,
        IReadOnlyDictionary<MemberDeclarationLocationKey, EntityId> memberIdByDeclarationLocation,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation,
        Dictionary<string, EntityId> externalStubIdByReference,
        Dictionary<string, EntityId> unresolvedCallTargetIdByText)
    {
        var declarationDependenciesByTypeId = new Dictionary<EntityId, HashSet<EntityId>>();
        var methodBodyDependenciesByTypeId = new Dictionary<EntityId, HashSet<EntityId>>();
        var emittedMethodMetricIds = new HashSet<EntityId>();
        var triples = new List<SemanticTriple>();
        var diagnostics = new List<IngestionDiagnostic>();

        foreach (var relativePath in projectBatch.SourceFiles)
        {
            if (!semanticContext.TryGetSemanticModel(relativePath, out var semanticModel, out _))
            {
                continue;
            }

            var root = semanticModel.SyntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            AddDeclarationDependenciesByType(
                root,
                semanticModel,
                typeIdByQualifiedName,
                externalStubIdByReference,
                declarationDependenciesByTypeId,
                triples);

            foreach (var methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var sourceMethodId = ResolveSourceMethodId(methodDeclaration.Identifier.GetLocation(), methodIdByDeclarationLocation);
                if (sourceMethodId == default)
                {
                    continue;
                }

                AddOverrideRelationshipForMethod(
                    methodDeclaration,
                    sourceMethodId,
                    semanticModel,
                    methodIdByDeclarationLocation,
                    triples,
                    diagnostics);

                if (methodDeclaration.Body is null && methodDeclaration.ExpressionBody is null)
                {
                    continue;
                }

                AddCallEdgesForInvocations(
                    methodDeclaration,
                    sourceMethodId,
                    semanticModel,
                    typeIdByQualifiedName,
                    methodCandidatesByTypeId,
                    externalStubIdByReference,
                    unresolvedCallTargetIdByText,
                    triples,
                    diagnostics);

                AddMemberReadWriteEdges(
                    methodDeclaration,
                    sourceMethodId,
                    semanticModel,
                    memberIdByDeclarationLocation,
                    triples);

                var methodBodyDependencies = AddMethodBodyDependencyEdges(
                    methodDeclaration,
                    sourceMethodId,
                    semanticModel,
                    typeIdByQualifiedName,
                    externalStubIdByReference,
                    triples);
                AppendTypeDependencies(methodBodyDependenciesByTypeId, declaringTypeIdByMethodId, sourceMethodId, methodBodyDependencies);

                if (emittedMethodMetricIds.Add(sourceMethodId))
                {
                    EmitMethodMetricTriples(methodDeclaration, sourceMethodId, semanticModel, triples);
                }
            }

            foreach (var constructorDeclaration in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                if (constructorDeclaration.Body is null && constructorDeclaration.ExpressionBody is null)
                {
                    continue;
                }

                var sourceMethodId = ResolveSourceMethodId(constructorDeclaration.Identifier.GetLocation(), methodIdByDeclarationLocation);
                if (sourceMethodId == default)
                {
                    continue;
                }

                AddCallEdgesForInvocations(
                    constructorDeclaration,
                    sourceMethodId,
                    semanticModel,
                    typeIdByQualifiedName,
                    methodCandidatesByTypeId,
                    externalStubIdByReference,
                    unresolvedCallTargetIdByText,
                    triples,
                    diagnostics);

                AddMemberReadWriteEdges(
                    constructorDeclaration,
                    sourceMethodId,
                    semanticModel,
                    memberIdByDeclarationLocation,
                    triples);

                var methodBodyDependencies = AddMethodBodyDependencyEdges(
                    constructorDeclaration,
                    sourceMethodId,
                    semanticModel,
                    typeIdByQualifiedName,
                    externalStubIdByReference,
                    triples);
                AppendTypeDependencies(methodBodyDependenciesByTypeId, declaringTypeIdByMethodId, sourceMethodId, methodBodyDependencies);

                if (emittedMethodMetricIds.Add(sourceMethodId))
                {
                    EmitMethodMetricTriples(constructorDeclaration, sourceMethodId, semanticModel, triples);
                }
            }
        }

        return new SemanticCallProjectBatchResult(
            projectBatch.ProjectSortKey,
            triples,
            diagnostics,
            declarationDependenciesByTypeId,
            methodBodyDependenciesByTypeId);
    }

    private static IReadOnlyList<SemanticCallProjectBatch> BuildSemanticCallProjectBatches(
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyDictionary<string, string> projectAssemblyNameByPath)
    {
        var projectContexts = projectAssemblyNameByPath.Keys
            .Select(projectPath => new
            {
                ProjectPath = Path.GetFullPath(projectPath),
                ProjectSortKey = ToRelativePath(repositoryRoot, Path.GetFullPath(projectPath)),
                RelativeDirectory = ToRelativePath(repositoryRoot, Path.GetDirectoryName(projectPath) ?? string.Empty),
            })
            .OrderByDescending(x => x.RelativeDirectory.Length)
            .ThenBy(x => x.RelativeDirectory, StringComparer.Ordinal)
            .ToArray();

        var filesByProjectSortKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var fallbackFiles = new List<string>();

        foreach (var relativePath in sourceFiles.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            string? matchedProjectSortKey = null;
            foreach (var context in projectContexts)
            {
                if (IsSourceFileUnderProjectDirectory(relativePath, context.RelativeDirectory))
                {
                    matchedProjectSortKey = context.ProjectSortKey;
                    break;
                }
            }

            if (matchedProjectSortKey is null)
            {
                fallbackFiles.Add(relativePath);
                continue;
            }

            if (!filesByProjectSortKey.TryGetValue(matchedProjectSortKey, out var files))
            {
                files = [];
                filesByProjectSortKey[matchedProjectSortKey] = files;
            }

            files.Add(relativePath);
        }

        var batches = filesByProjectSortKey
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new SemanticCallProjectBatch(
                x.Key,
                x.Value.OrderBy(y => y, StringComparer.Ordinal).ToArray()))
            .ToList();

        if (fallbackFiles.Count > 0)
        {
            batches.Add(new SemanticCallProjectBatch(
                "_fallback",
                fallbackFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
        }

        return batches;
    }

    private static bool IsSourceFileUnderProjectDirectory(string relativeSourceFilePath, string relativeProjectDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeProjectDirectory) || relativeProjectDirectory == ".")
        {
            return true;
        }

        return relativeSourceFilePath.Equals(relativeProjectDirectory, StringComparison.Ordinal)
               || relativeSourceFilePath.StartsWith(relativeProjectDirectory + "/", StringComparison.Ordinal);
    }

    private static void MergeTypeDependencyMap(
        Dictionary<EntityId, HashSet<EntityId>> target,
        IReadOnlyDictionary<EntityId, HashSet<EntityId>> source)
    {
        foreach (var pair in source)
        {
            if (!target.TryGetValue(pair.Key, out var targetSet))
            {
                targetSet = [];
                target[pair.Key] = targetSet;
            }

            targetSet.UnionWith(pair.Value);
        }
    }

    private static string CreateDeterministicTripleSortKey(SemanticTriple triple)
    {
        return $"{CreateDeterministicNodeSortKey(triple.Subject)}|{triple.Predicate.Value}|{CreateDeterministicNodeSortKey(triple.Object)}";
    }

    private static string CreateDeterministicNodeSortKey(GraphNode node)
    {
        return node switch
        {
            EntityNode entity => $"E:{entity.Id.Value}",
            LiteralNode literal => $"L:{literal.Value?.ToString() ?? string.Empty}",
            _ => node.ToString() ?? string.Empty,
        };
    }

    private static void AppendTypeDependencies(
        Dictionary<EntityId, HashSet<EntityId>> dependenciesByTypeId,
        IReadOnlyDictionary<EntityId, EntityId> declaringTypeIdByMethodId,
        EntityId sourceMethodId,
        IReadOnlyCollection<EntityId> dependencyTargetIds)
    {
        if (dependencyTargetIds.Count == 0 || !declaringTypeIdByMethodId.TryGetValue(sourceMethodId, out var declaringTypeId))
        {
            return;
        }

        if (!dependenciesByTypeId.TryGetValue(declaringTypeId, out var targets))
        {
            targets = [];
            dependenciesByTypeId[declaringTypeId] = targets;
        }

        foreach (var targetId in dependencyTargetIds)
        {
            targets.Add(targetId);
        }
    }

    private void AddDeclarationDependenciesByType(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        Dictionary<string, EntityId> externalStubIdByReference,
        Dictionary<EntityId, HashSet<EntityId>> declarationDependenciesByTypeId,
        List<SemanticTriple> triples)
    {
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(declaration);
            if (declaredSymbol is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            var qualifiedTypeName = GetTypeQualifiedName(typeSymbol);
            if (!typeIdByQualifiedName.TryGetValue(qualifiedTypeName, out var typeId))
            {
                continue;
            }

            if (!declarationDependenciesByTypeId.TryGetValue(typeId, out var dependencies))
            {
                dependencies = [];
                declarationDependenciesByTypeId[typeId] = dependencies;
            }

            if (typeSymbol.BaseType is not null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                AddCouplingDependencies(typeSymbol.BaseType, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
            }

            foreach (var interfaceType in typeSymbol.Interfaces)
            {
                AddCouplingDependencies(interfaceType, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
            }

            foreach (var member in typeSymbol.GetMembers().Where(x => !x.IsImplicitlyDeclared))
            {
                switch (member)
                {
                    case IFieldSymbol field:
                        AddCouplingDependencies(field.Type, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
                        break;
                    case IPropertySymbol property:
                        AddCouplingDependencies(property.Type, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
                        break;
                    case IEventSymbol eventSymbol:
                        AddCouplingDependencies(eventSymbol.Type, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
                        break;
                    case IMethodSymbol method when ShouldIncludeMethodDeclarationDependencies(method):
                        if (!method.ReturnsVoid)
                        {
                            AddCouplingDependencies(method.ReturnType, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
                        }

                        foreach (var parameter in method.Parameters)
                        {
                            AddCouplingDependencies(parameter.Type, typeIdByQualifiedName, externalStubIdByReference, triples, dependencies);
                        }

                        break;
                }
            }
        }
    }

    private static bool ShouldIncludeMethodDeclarationDependencies(IMethodSymbol method)
    {
        if (method.IsImplicitlyDeclared)
        {
            return false;
        }

        return method.MethodKind is MethodKind.Ordinary
            or MethodKind.Constructor
            or MethodKind.StaticConstructor
            or MethodKind.Conversion
            or MethodKind.UserDefinedOperator
            or MethodKind.Destructor;
    }

    private static void EmitCboMetricsByType(
        IEnumerable<EntityId> typeIds,
        IReadOnlyDictionary<EntityId, HashSet<EntityId>> declarationDependenciesByTypeId,
        IReadOnlyDictionary<EntityId, HashSet<EntityId>> methodBodyDependenciesByTypeId,
        List<SemanticTriple> triples)
    {
        foreach (var typeId in typeIds.Distinct().OrderBy(x => x.Value, StringComparer.Ordinal))
        {
            var declarationDependencies = declarationDependenciesByTypeId.TryGetValue(typeId, out var declarationSet)
                ? new HashSet<EntityId>(declarationSet)
                : [];
            var methodBodyDependencies = methodBodyDependenciesByTypeId.TryGetValue(typeId, out var methodBodySet)
                ? new HashSet<EntityId>(methodBodySet)
                : [];

            declarationDependencies.Remove(typeId);
            methodBodyDependencies.Remove(typeId);

            var totalDependencies = new HashSet<EntityId>(declarationDependencies);
            totalDependencies.UnionWith(methodBodyDependencies);

            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.CboDeclaration,
                new LiteralNode(declarationDependencies.Count.ToString(CultureInfo.InvariantCulture))));
            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.CboMethodBody,
                new LiteralNode(methodBodyDependencies.Count.ToString(CultureInfo.InvariantCulture))));
            triples.Add(new SemanticTriple(
                new EntityNode(typeId),
                CorePredicates.CboTotal,
                new LiteralNode(totalDependencies.Count.ToString(CultureInfo.InvariantCulture))));
        }
    }

    private static void EmitMethodMetricTriples(
        MemberDeclarationSyntax declaration,
        EntityId sourceMethodId,
        SemanticModel semanticModel,
        List<SemanticTriple> triples)
    {
        var metricRoot = GetMetricRoot(declaration, semanticModel);
        if (metricRoot is null)
        {
            return;
        }

        var cyclomatic = CalculateCyclomaticComplexity(metricRoot);
        var cognitive = CalculateCognitiveComplexity(metricRoot);
        var halstead = CalculateHalsteadMetrics(metricRoot);
        var loc = CalculateLinesOfCode(metricRoot);
        var maintainabilityIndex = CalculateMaintainabilityIndex(halstead.Volume, cyclomatic, Math.Max(1, loc.CodeLines));

        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.CyclomaticComplexity, new LiteralNode(cyclomatic.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.CognitiveComplexity, new LiteralNode(cognitive.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadDistinctOperators, new LiteralNode(halstead.DistinctOperators.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadDistinctOperands, new LiteralNode(halstead.DistinctOperands.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadTotalOperators, new LiteralNode(halstead.TotalOperators.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadTotalOperands, new LiteralNode(halstead.TotalOperands.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadVocabulary, new LiteralNode(halstead.Vocabulary.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadLength, new LiteralNode(halstead.Length.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadVolume, new LiteralNode(FormatDouble(halstead.Volume))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadDifficulty, new LiteralNode(FormatDouble(halstead.Difficulty))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadEffort, new LiteralNode(FormatDouble(halstead.Effort))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadEstimatedBugs, new LiteralNode(FormatDouble(halstead.EstimatedBugs))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.HalsteadEstimatedTimeSeconds, new LiteralNode(FormatDouble(halstead.EstimatedTimeSeconds))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.LocTotalLines, new LiteralNode(loc.TotalLines.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.LocCodeLines, new LiteralNode(loc.CodeLines.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.LocCommentLines, new LiteralNode(loc.CommentLines.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.LocBlankLines, new LiteralNode(loc.BlankLines.ToString(CultureInfo.InvariantCulture))));
        triples.Add(new SemanticTriple(new EntityNode(sourceMethodId), CorePredicates.MaintainabilityIndex, new LiteralNode(FormatDouble(maintainabilityIndex))));
    }

    private static SyntaxNode? GetMetricRoot(MemberDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method when method.Body is not null => method.Body,
            MethodDeclarationSyntax method when method.ExpressionBody is not null
                => semanticModel.GetOperation(method.ExpressionBody.Expression)?.Syntax,
            ConstructorDeclarationSyntax ctor when ctor.Body is not null => ctor.Body,
            ConstructorDeclarationSyntax ctor when ctor.ExpressionBody is not null
                => semanticModel.GetOperation(ctor.ExpressionBody.Expression)?.Syntax,
            _ => null,
        };
    }

    private static int CalculateCyclomaticComplexity(SyntaxNode root)
    {
        var decisionPoints = root.DescendantNodesAndSelf().Count(node =>
            node is IfStatementSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or CatchClauseSyntax
                or ConditionalExpressionSyntax
                or SwitchExpressionArmSyntax
                or CaseSwitchLabelSyntax
                or CasePatternSwitchLabelSyntax
                or ConditionalAccessExpressionSyntax
                || (node is BinaryExpressionSyntax binary
                    && (binary.IsKind(SyntaxKind.LogicalAndExpression)
                        || binary.IsKind(SyntaxKind.LogicalOrExpression))));

        return 1 + decisionPoints;
    }

    private static int CalculateCognitiveComplexity(SyntaxNode root)
    {
        var score = 0;

        void Visit(SyntaxNode node, int nesting)
        {
            switch (node)
            {
                case IfStatementSyntax ifStatement:
                    score += 1 + nesting;
                    score += CountLogicalOperators(ifStatement.Condition);
                    Visit(ifStatement.Statement, nesting + 1);
                    if (ifStatement.Else is not null)
                    {
                        if (ifStatement.Else.Statement is IfStatementSyntax elseIf)
                        {
                            Visit(elseIf, nesting);
                        }
                        else
                        {
                            Visit(ifStatement.Else.Statement, nesting + 1);
                        }
                    }
                    return;
                case ForStatementSyntax forStatement:
                    score += 1 + nesting;
                    if (forStatement.Condition is not null)
                    {
                        score += CountLogicalOperators(forStatement.Condition);
                    }

                    Visit(forStatement.Statement, nesting + 1);
                    return;
                case ForEachStatementSyntax forEachStatement:
                    score += 1 + nesting;
                    Visit(forEachStatement.Statement, nesting + 1);
                    return;
                case WhileStatementSyntax whileStatement:
                    score += 1 + nesting;
                    score += CountLogicalOperators(whileStatement.Condition);
                    Visit(whileStatement.Statement, nesting + 1);
                    return;
                case DoStatementSyntax doStatement:
                    score += 1 + nesting;
                    score += CountLogicalOperators(doStatement.Condition);
                    Visit(doStatement.Statement, nesting + 1);
                    return;
                case SwitchStatementSyntax switchStatement:
                    score += 1 + nesting;
                    foreach (var section in switchStatement.Sections)
                    {
                        foreach (var label in section.Labels.Where(label => label is not DefaultSwitchLabelSyntax))
                        {
                            _ = label;
                            score += 1;
                        }

                        foreach (var statement in section.Statements)
                        {
                            Visit(statement, nesting + 1);
                        }
                    }

                    return;
                case SwitchExpressionSyntax switchExpression:
                    score += 1 + nesting;
                    foreach (var arm in switchExpression.Arms)
                    {
                        score += 1 + nesting;
                        Visit(arm.Expression, nesting + 1);
                    }

                    return;
                case CatchClauseSyntax catchClause:
                    score += 1 + nesting;
                    if (catchClause.Filter is not null)
                    {
                        score += CountLogicalOperators(catchClause.Filter.FilterExpression);
                    }

                    Visit(catchClause.Block, nesting + 1);
                    return;
                case ConditionalExpressionSyntax conditionalExpression:
                    score += 1 + nesting;
                    score += CountLogicalOperators(conditionalExpression.Condition);
                    Visit(conditionalExpression.WhenTrue, nesting + 1);
                    Visit(conditionalExpression.WhenFalse, nesting + 1);
                    return;
                case ParenthesizedLambdaExpressionSyntax lambdaExpression:
                    Visit(lambdaExpression.Body, nesting + 1);
                    return;
                case SimpleLambdaExpressionSyntax simpleLambdaExpression:
                    Visit(simpleLambdaExpression.Body, nesting + 1);
                    return;
                case LocalFunctionStatementSyntax localFunction:
                    score += 1 + nesting;
                    if (localFunction.Body is not null)
                    {
                        Visit(localFunction.Body, nesting + 1);
                    }
                    else if (localFunction.ExpressionBody is not null)
                    {
                        Visit(localFunction.ExpressionBody.Expression, nesting + 1);
                    }
                    return;
            }

            foreach (var child in node.ChildNodes())
            {
                Visit(child, nesting);
            }
        }

        Visit(root, 0);
        return score;
    }

    private static int CountLogicalOperators(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<BinaryExpressionSyntax>()
            .Count(binary => binary.IsKind(SyntaxKind.LogicalAndExpression)
                || binary.IsKind(SyntaxKind.LogicalOrExpression));
    }

    private static HalsteadMetricSnapshot CalculateHalsteadMetrics(SyntaxNode root)
    {
        var distinctOperators = new HashSet<string>(StringComparer.Ordinal);
        var distinctOperands = new HashSet<string>(StringComparer.Ordinal);
        var totalOperators = 0;
        var totalOperands = 0;

        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            if (IsOperandToken(token))
            {
                totalOperands++;
                distinctOperands.Add(token.ValueText);
                continue;
            }

            if (IsOperatorToken(token))
            {
                totalOperators++;
                distinctOperators.Add(token.Text);
            }
        }

        var n1 = distinctOperators.Count;
        var n2 = distinctOperands.Count;
        var n = n1 + n2;
        var n1Safe = Math.Max(1, n1);
        var n2Safe = Math.Max(1, n2);
        var length = totalOperators + totalOperands;
        var volume = length <= 0 || n <= 0 ? 0d : length * Math.Log2(n);
        var difficulty = (n1Safe / 2d) * (totalOperands / (double)n2Safe);
        var effort = difficulty * volume;
        var estimatedBugs = volume / 3000d;
        var estimatedTimeSeconds = effort / 18d;

        return new HalsteadMetricSnapshot(
            DistinctOperators: n1,
            DistinctOperands: n2,
            TotalOperators: totalOperators,
            TotalOperands: totalOperands,
            Vocabulary: n,
            Length: length,
            Volume: volume,
            Difficulty: difficulty,
            Effort: effort,
            EstimatedBugs: estimatedBugs,
            EstimatedTimeSeconds: estimatedTimeSeconds);
    }

    private static bool IsOperandToken(SyntaxToken token)
    {
        return token.Kind() is SyntaxKind.IdentifierToken
            or SyntaxKind.NumericLiteralToken
            or SyntaxKind.StringLiteralToken
            or SyntaxKind.CharacterLiteralToken
            or SyntaxKind.TrueKeyword
            or SyntaxKind.FalseKeyword
            or SyntaxKind.NullKeyword;
    }

    private static bool IsOperatorToken(SyntaxToken token)
    {
        return token.Kind() is SyntaxKind.PlusToken
            or SyntaxKind.MinusToken
            or SyntaxKind.AsteriskToken
            or SyntaxKind.SlashToken
            or SyntaxKind.PercentToken
            or SyntaxKind.AmpersandToken
            or SyntaxKind.BarToken
            or SyntaxKind.CaretToken
            or SyntaxKind.TildeToken
            or SyntaxKind.ExclamationToken
            or SyntaxKind.EqualsToken
            or SyntaxKind.LessThanToken
            or SyntaxKind.GreaterThanToken
            or SyntaxKind.LessThanEqualsToken
            or SyntaxKind.GreaterThanEqualsToken
            or SyntaxKind.EqualsEqualsToken
            or SyntaxKind.ExclamationEqualsToken
            or SyntaxKind.AmpersandAmpersandToken
            or SyntaxKind.BarBarToken
            or SyntaxKind.PlusPlusToken
            or SyntaxKind.MinusMinusToken
            or SyntaxKind.MinusGreaterThanToken
            or SyntaxKind.QuestionToken
            or SyntaxKind.QuestionQuestionToken
            or SyntaxKind.QuestionQuestionEqualsToken
            or SyntaxKind.PlusEqualsToken
            or SyntaxKind.MinusEqualsToken
            or SyntaxKind.AsteriskEqualsToken
            or SyntaxKind.SlashEqualsToken
            or SyntaxKind.PercentEqualsToken
            or SyntaxKind.AmpersandEqualsToken
            or SyntaxKind.BarEqualsToken
            or SyntaxKind.CaretEqualsToken
            or SyntaxKind.LessThanLessThanToken
            or SyntaxKind.GreaterThanGreaterThanToken
            or SyntaxKind.LessThanLessThanEqualsToken
            or SyntaxKind.GreaterThanGreaterThanEqualsToken
            or SyntaxKind.IfKeyword
            or SyntaxKind.ElseKeyword
            or SyntaxKind.SwitchKeyword
            or SyntaxKind.CaseKeyword
            or SyntaxKind.ForKeyword
            or SyntaxKind.ForEachKeyword
            or SyntaxKind.WhileKeyword
            or SyntaxKind.DoKeyword
            or SyntaxKind.ReturnKeyword
            or SyntaxKind.NewKeyword
            or SyntaxKind.ThrowKeyword
            or SyntaxKind.TryKeyword
            or SyntaxKind.CatchKeyword
            or SyntaxKind.FinallyKeyword
            or SyntaxKind.LockKeyword
            or SyntaxKind.UsingKeyword
            or SyntaxKind.AwaitKeyword
            or SyntaxKind.IsKeyword
            or SyntaxKind.AsKeyword;
    }

    private static LocMetricSnapshot CalculateLinesOfCode(SyntaxNode root)
    {
        var sourceText = root.SyntaxTree.GetText();
        var lineSpan = root.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line;
        var endLine = lineSpan.EndLinePosition.Line;

        var total = 0;
        var blank = 0;
        var comment = 0;
        var code = 0;

        for (var lineIndex = startLine; lineIndex <= endLine; lineIndex++)
        {
            var lineText = sourceText.Lines[lineIndex].ToString();
            var trimmed = lineText.Trim();
            total++;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                blank++;
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal)
                || trimmed.StartsWith("*/", StringComparison.Ordinal))
            {
                comment++;
                continue;
            }

            code++;
        }

        return new LocMetricSnapshot(total, code, comment, blank);
    }

    private static double CalculateMaintainabilityIndex(double halsteadVolume, int cyclomaticComplexity, int locCodeLines)
    {
        var safeVolume = Math.Max(1d, halsteadVolume);
        var safeLoc = Math.Max(1d, locCodeLines);
        var index = (171d
                    - (5.2d * Math.Log(safeVolume))
                    - (0.23d * cyclomaticComplexity)
                    - (16.2d * Math.Log(safeLoc)))
                    * 100d
                    / 171d;

        return Math.Clamp(index, 0d, 100d);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static int CountPredicateOccurrences(IReadOnlyList<SemanticTriple> triples, PredicateId predicate)
    {
        return triples.Count(x => x.Predicate == predicate);
    }

    private static void AddDeduplicatedTypeResolutionFallbackDiagnostic(
        List<IngestionDiagnostic> diagnostics,
        HashSet<string> emittedFallbackDiagnosticKeys,
        string dedupeScope,
        string referenceName,
        string message)
    {
        var key = $"{dedupeScope}:{NormalizeTypeReferenceName(referenceName)}";
        if (!emittedFallbackDiagnosticKeys.Add(key))
        {
            return;
        }

        diagnostics.Add(new IngestionDiagnostic("type:resolution:fallback", message));
    }

    private static void AddOverrideRelationshipForMethod(
        MethodDeclarationSyntax methodDeclaration,
        EntityId sourceMethodId,
        SemanticModel semanticModel,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics)
    {
        var sourceSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        var overriddenMethod = sourceSymbol?.OverriddenMethod;
        if (overriddenMethod is null)
        {
            return;
        }

        var targetMethodId = ResolveDeclaredMethodId(overriddenMethod, methodIdByDeclarationLocation);
        if (targetMethodId is null)
        {
            diagnostics.Add(new IngestionDiagnostic(
                "method:relationship:override:unresolved",
                $"Override target could not be resolved for method '{sourceMethodId.Value}'."));
            return;
        }

        triples.Add(new SemanticTriple(
            new EntityNode(sourceMethodId),
            CorePredicates.OverridesMethod,
            new EntityNode(targetMethodId.Value)));
    }

    private static EntityId? ResolveDeclaredMethodId(
        IMethodSymbol methodSymbol,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation)
    {
        foreach (var location in methodSymbol.Locations.Where(x => x.IsInSource))
        {
            var lineSpan = location.GetLineSpan();
            var key = new MethodDeclarationLocationKey(
                location.SourceTree?.FilePath ?? string.Empty,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1);

            if (methodIdByDeclarationLocation.TryGetValue(key, out var methodId))
            {
                return methodId;
            }
        }

        return null;
    }

    private void AddCallEdgesForInvocations(
        MemberDeclarationSyntax declaration,
        EntityId sourceMethodId,
        SemanticModel semanticModel,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        IReadOnlyDictionary<EntityId, List<MethodRelationshipCandidate>> methodCandidatesByTypeId,
        Dictionary<string, EntityId> externalStubIdByReference,
        Dictionary<string, EntityId> unresolvedCallTargetIdByText,
        List<SemanticTriple> triples,
        List<IngestionDiagnostic> diagnostics)
    {
        var invocations = declaration
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToArray();

        foreach (var invocation in invocations)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var calledSymbol = symbolInfo.Symbol as IMethodSymbol
                               ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

            if (calledSymbol is null)
            {
                var unresolvedTarget = invocation.Expression.ToString().Trim();
                if (string.IsNullOrWhiteSpace(unresolvedTarget))
                {
                    unresolvedTarget = invocation.ToString().Trim();
                }

                var unresolvedTargetId = GetOrCreateUnresolvedCallTargetId(
                    unresolvedTarget,
                    "symbol-unresolved",
                    unresolvedCallTargetIdByText,
                    triples);
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    CorePredicates.Calls,
                    new EntityNode(unresolvedTargetId)));
                foreach (var diagnostic in _callResolutionDiagnosticClassifier.Classify(
                             new CallResolutionFailureContext(
                                 CallResolutionFailureCause.SymbolUnresolved,
                                 sourceMethodId.Value,
                                 invocation.ToString().Trim())))
                {
                    diagnostics.Add(diagnostic);
                }
                continue;
            }

            var targetSymbol = calledSymbol.ReducedFrom ?? calledSymbol;
            if (targetSymbol.ContainingType is null)
            {
                var unresolvedTarget = invocation.Expression.ToString().Trim();
                if (string.IsNullOrWhiteSpace(unresolvedTarget))
                {
                    unresolvedTarget = invocation.ToString().Trim();
                }

                var unresolvedTargetId = GetOrCreateUnresolvedCallTargetId(
                    unresolvedTarget,
                    "missing-containing-type",
                    unresolvedCallTargetIdByText,
                    triples);
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    CorePredicates.Calls,
                    new EntityNode(unresolvedTargetId)));
                foreach (var diagnostic in _callResolutionDiagnosticClassifier.Classify(
                             new CallResolutionFailureContext(
                                 CallResolutionFailureCause.MissingContainingType,
                                 sourceMethodId.Value,
                                 invocation.ToString().Trim())))
                {
                    diagnostics.Add(diagnostic);
                }
                continue;
            }

            var targetTypeQualifiedName = GetTypeQualifiedName(targetSymbol.ContainingType);
            if (typeIdByQualifiedName.TryGetValue(targetTypeQualifiedName, out var targetTypeId))
            {
                if (methodCandidatesByTypeId.TryGetValue(targetTypeId, out var targetCandidates))
                {
                    var targetMethodId = ResolveTargetMethodId(targetSymbol, targetCandidates);
                    if (targetMethodId is not null)
                    {
                        triples.Add(new SemanticTriple(
                            new EntityNode(sourceMethodId),
                            CorePredicates.Calls,
                            new EntityNode(targetMethodId.Value)));
                        continue;
                    }
                }

                var unresolvedTarget = invocation.Expression.ToString().Trim();
                if (string.IsNullOrWhiteSpace(unresolvedTarget))
                {
                    unresolvedTarget = invocation.ToString().Trim();
                }

                var unresolvedTargetId = GetOrCreateUnresolvedCallTargetId(
                    unresolvedTarget,
                    "internal-target-unmatched",
                    unresolvedCallTargetIdByText,
                    triples);
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    CorePredicates.Calls,
                    new EntityNode(unresolvedTargetId)));
                diagnostics.Add(new IngestionDiagnostic(
                    "method:call:internal-target-unmatched",
                    $"Invocation '{invocation}' resolved to internal type '{targetTypeQualifiedName}' but no unique method declaration could be matched in method '{sourceMethodId.Value}'."));
                continue;
            }

            var externalTypeName = targetSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var externalTypeId = GetOrCreateExternalStubId(externalTypeName, externalStubIdByReference, triples);
            if (!string.IsNullOrWhiteSpace(targetSymbol.ContainingAssembly?.Name))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(externalTypeId),
                    CorePredicates.ExternalAssemblyName,
                    new LiteralNode(targetSymbol.ContainingAssembly!.Name)));
            }

            triples.Add(new SemanticTriple(
                new EntityNode(sourceMethodId),
                CorePredicates.Calls,
                new EntityNode(externalTypeId)));
        }
    }

    private static void AddMemberReadWriteEdges(
        MemberDeclarationSyntax declaration,
        EntityId sourceMethodId,
        SemanticModel semanticModel,
        IReadOnlyDictionary<MemberDeclarationLocationKey, EntityId> memberIdByDeclarationLocation,
        List<SemanticTriple> triples)
    {
        var operationRoot = GetOperationRootForBody(declaration, semanticModel);
        if (operationRoot is null)
        {
            return;
        }

        var emitted = new HashSet<(PredicateId Predicate, EntityId TargetMember)>();

        foreach (var operation in EnumerateOperations(operationRoot))
        {
            switch (operation)
            {
                case IPropertyReferenceOperation propertyReference:
                    EmitMemberReadWriteTriples(
                        propertyReference,
                        propertyReference.Property,
                        isField: false,
                        sourceMethodId,
                        memberIdByDeclarationLocation,
                        emitted,
                        triples);
                    break;
                case IFieldReferenceOperation fieldReference when !fieldReference.Field.IsImplicitlyDeclared:
                    EmitMemberReadWriteTriples(
                        fieldReference,
                        fieldReference.Field,
                        isField: true,
                        sourceMethodId,
                        memberIdByDeclarationLocation,
                        emitted,
                        triples);
                    break;
            }
        }
    }

    private IReadOnlyCollection<EntityId> AddMethodBodyDependencyEdges(
        MemberDeclarationSyntax declaration,
        EntityId sourceMethodId,
        SemanticModel semanticModel,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        Dictionary<string, EntityId> externalStubIdByReference,
        List<SemanticTriple> triples)
    {
        var operationRoot = GetOperationRootForBody(declaration, semanticModel);
        if (operationRoot is null)
        {
            return [];
        }

        var emittedDependencyTargetIds = new HashSet<EntityId>();
        var methodBodyCouplingTargetIds = new HashSet<EntityId>();
        foreach (var operation in EnumerateOperations(operationRoot))
        {
            if (IsWithinNameof(operation))
            {
                continue;
            }

            var candidateType = TryGetMethodBodyDependencyType(operation);
            if (candidateType is null)
            {
                continue;
            }

            if (!TryResolveMethodBodyDependencyTargetId(
                    candidateType,
                    typeIdByQualifiedName,
                    externalStubIdByReference,
                    triples,
                    out var dependencyTargetId))
            {
                continue;
            }

            if (emittedDependencyTargetIds.Add(dependencyTargetId))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    CorePredicates.DependsOnTypeInMethodBody,
                    new EntityNode(dependencyTargetId)));
            }

            AddCouplingDependencies(
                candidateType,
                typeIdByQualifiedName,
                externalStubIdByReference,
                triples,
                methodBodyCouplingTargetIds);
        }

        foreach (var typeSyntax in EnumerateMethodBodyDependencyTypeSyntax(declaration))
        {
            if (IsWithinNameof(typeSyntax))
            {
                continue;
            }

            var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
            var candidateType = typeInfo.Type;
            if (candidateType is null)
            {
                continue;
            }

            if (!TryResolveMethodBodyDependencyTargetId(
                    candidateType,
                    typeIdByQualifiedName,
                    externalStubIdByReference,
                    triples,
                    out var dependencyTargetId))
            {
                continue;
            }

            if (emittedDependencyTargetIds.Add(dependencyTargetId))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    CorePredicates.DependsOnTypeInMethodBody,
                    new EntityNode(dependencyTargetId)));
            }

            AddCouplingDependencies(
                candidateType,
                typeIdByQualifiedName,
                externalStubIdByReference,
                triples,
                methodBodyCouplingTargetIds);
        }

        return methodBodyCouplingTargetIds;
    }

    private static IEnumerable<TypeSyntax> EnumerateMethodBodyDependencyTypeSyntax(MemberDeclarationSyntax declaration)
    {
        foreach (var castExpression in declaration.DescendantNodes().OfType<CastExpressionSyntax>())
        {
            yield return castExpression.Type;
        }

        foreach (var binaryExpression in declaration.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (binaryExpression.IsKind(SyntaxKind.IsExpression)
                || binaryExpression.IsKind(SyntaxKind.AsExpression))
            {
                if (binaryExpression.Right is TypeSyntax typeSyntax)
                {
                    yield return typeSyntax;
                }
            }
        }

        foreach (var typeOfExpression in declaration.DescendantNodes().OfType<TypeOfExpressionSyntax>())
        {
            yield return typeOfExpression.Type;
        }
    }

    private static bool IsWithinNameof(SyntaxNode node)
    {
        return node.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(static invocation =>
                invocation.Expression is IdentifierNameSyntax identifier
                && string.Equals(identifier.Identifier.ValueText, "nameof", StringComparison.Ordinal));
    }

    private static bool IsWithinNameof(IOperation operation)
    {
        var current = operation;
        while (current is not null)
        {
            if (current is INameOfOperation)
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static ITypeSymbol? TryGetMethodBodyDependencyType(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod?.ContainingType,
            IObjectCreationOperation creation => creation.Type,
            IPropertyReferenceOperation propertyReference => propertyReference.Property?.ContainingType,
            IFieldReferenceOperation fieldReference when !fieldReference.Field.IsImplicitlyDeclared => fieldReference.Field.ContainingType,
            IMethodReferenceOperation methodReference => methodReference.Method?.ContainingType,
            ITypeOfOperation typeOfOperation => typeOfOperation.TypeOperand,
            _ => null,
        };
    }

    private bool TryResolveMethodBodyDependencyTargetId(
        ITypeSymbol candidateType,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        Dictionary<string, EntityId> externalStubIdByReference,
        List<SemanticTriple> triples,
        out EntityId targetId)
    {
        var normalizedSymbol = NormalizeMethodBodyDependencyTypeSymbol(candidateType);
        if (normalizedSymbol is null)
        {
            targetId = default;
            return false;
        }

        var qualifiedTypeName = GetTypeQualifiedName(normalizedSymbol);
        if (typeIdByQualifiedName.TryGetValue(qualifiedTypeName, out var internalTypeId))
        {
            targetId = internalTypeId;
            return true;
        }

        var normalizedReferenceName = NormalizeTypeReferenceName(
            normalizedSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        if (!IsExternalStubCandidate(normalizedReferenceName))
        {
            targetId = default;
            return false;
        }

        targetId = GetOrCreateExternalStubId(normalizedReferenceName, externalStubIdByReference, triples);
        return true;
    }

    private static ITypeSymbol? NormalizeMethodBodyDependencyTypeSymbol(ITypeSymbol? typeSymbol)
    {
        while (typeSymbol is not null)
        {
            switch (typeSymbol)
            {
                case IArrayTypeSymbol arrayType:
                    typeSymbol = arrayType.ElementType;
                    continue;
                case IPointerTypeSymbol pointerType:
                    typeSymbol = pointerType.PointedAtType;
                    continue;
                case IDynamicTypeSymbol:
                    return null;
                case INamedTypeSymbol namedType when namedType.IsGenericType:
                    return namedType.ConstructedFrom;
                default:
                    return typeSymbol;
            }
        }

        return null;
    }

    private void AddCouplingDependencies(
        ITypeSymbol candidateType,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        Dictionary<string, EntityId> externalStubIdByReference,
        List<SemanticTriple> triples,
        HashSet<EntityId> targetIds)
    {
        foreach (var couplingType in EnumerateCouplingTypeSymbols(candidateType))
        {
            if (!TryResolveCouplingTargetId(
                    couplingType,
                    typeIdByQualifiedName,
                    externalStubIdByReference,
                    triples,
                    out var targetId))
            {
                continue;
            }

            targetIds.Add(targetId);
        }
    }

    private bool TryResolveCouplingTargetId(
        ITypeSymbol candidateType,
        IReadOnlyDictionary<string, EntityId> typeIdByQualifiedName,
        Dictionary<string, EntityId> externalStubIdByReference,
        List<SemanticTriple> triples,
        out EntityId targetId)
    {
        var qualifiedTypeName = GetTypeQualifiedName(candidateType);
        if (typeIdByQualifiedName.TryGetValue(qualifiedTypeName, out var internalTypeId))
        {
            targetId = internalTypeId;
            return true;
        }

        var normalizedReferenceName = NormalizeTypeReferenceName(
            candidateType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        if (!IsExternalStubCandidate(normalizedReferenceName))
        {
            targetId = default;
            return false;
        }

        targetId = GetOrCreateExternalStubId(normalizedReferenceName, externalStubIdByReference, triples);
        return true;
    }

    private static IReadOnlyList<ITypeSymbol> EnumerateCouplingTypeSymbols(ITypeSymbol? typeSymbol)
    {
        var collected = new List<ITypeSymbol>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        void Visit(ITypeSymbol? symbol)
        {
            if (symbol is null)
            {
                return;
            }

            switch (symbol)
            {
                case IDynamicTypeSymbol:
                case ITypeParameterSymbol:
                    return;
                case IArrayTypeSymbol arrayType:
                    Visit(arrayType.ElementType);
                    return;
                case IPointerTypeSymbol pointerType:
                    Visit(pointerType.PointedAtType);
                    return;
                case IFunctionPointerTypeSymbol functionPointerType:
                    Visit(functionPointerType.Signature.ReturnType);
                    foreach (var parameter in functionPointerType.Signature.Parameters)
                    {
                        Visit(parameter.Type);
                    }
                    return;
                case INamedTypeSymbol namedType:
                    if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                        && namedType.TypeArguments.Length == 1)
                    {
                        Visit(namedType.TypeArguments[0]);
                        return;
                    }

                    if (namedType.IsTupleType)
                    {
                        foreach (var element in namedType.TupleElements)
                        {
                            Visit(element.Type);
                        }

                        return;
                    }

                    var container = namedType.OriginalDefinition;
                    if (!IsBuiltInType(container))
                    {
                        var key = container.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (seenKeys.Add(key))
                        {
                            collected.Add(container);
                        }
                    }

                    foreach (var typeArgument in namedType.TypeArguments)
                    {
                        Visit(typeArgument);
                    }

                    return;
                default:
                    if (!IsBuiltInType(symbol))
                    {
                        var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (seenKeys.Add(key))
                        {
                            collected.Add(symbol);
                        }
                    }

                    return;
            }
        }

        Visit(typeSymbol);
        return collected;
    }

    private static bool IsBuiltInType(ITypeSymbol symbol)
    {
        if (symbol.SpecialType != SpecialType.None)
        {
            return true;
        }

        return symbol.TypeKind == TypeKind.Pointer
               || symbol.TypeKind == TypeKind.FunctionPointer;
    }

    private static void EmitMemberReadWriteTriples(
        IOperation memberReference,
        ISymbol symbol,
        bool isField,
        EntityId sourceMethodId,
        IReadOnlyDictionary<MemberDeclarationLocationKey, EntityId> memberIdByDeclarationLocation,
        HashSet<(PredicateId Predicate, EntityId TargetMember)> emitted,
        List<SemanticTriple> triples)
    {
        var targetMemberId = ResolveInternalMemberId(symbol, memberIdByDeclarationLocation);
        if (targetMemberId is null)
        {
            return;
        }

        var (isRead, isWrite) = DetermineReferenceAccess(memberReference);

        if (isRead)
        {
            var predicate = isField ? CorePredicates.ReadsField : CorePredicates.ReadsProperty;
            if (emitted.Add((predicate, targetMemberId.Value)))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    predicate,
                    new EntityNode(targetMemberId.Value)));
            }
        }

        if (isWrite)
        {
            var predicate = isField ? CorePredicates.WritesField : CorePredicates.WritesProperty;
            if (emitted.Add((predicate, targetMemberId.Value)))
            {
                triples.Add(new SemanticTriple(
                    new EntityNode(sourceMethodId),
                    predicate,
                    new EntityNode(targetMemberId.Value)));
            }
        }
    }

    private static IOperation? GetOperationRootForBody(
        MemberDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method when method.Body is not null => semanticModel.GetOperation(method.Body),
            MethodDeclarationSyntax method when method.ExpressionBody is not null => semanticModel.GetOperation(method.ExpressionBody.Expression),
            ConstructorDeclarationSyntax ctor when ctor.Body is not null => semanticModel.GetOperation(ctor.Body),
            ConstructorDeclarationSyntax ctor when ctor.ExpressionBody is not null => semanticModel.GetOperation(ctor.ExpressionBody.Expression),
            _ => null,
        };
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root)
    {
        var stack = new Stack<IOperation>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            foreach (var child in current.ChildOperations.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    private static (bool IsRead, bool IsWrite) DetermineReferenceAccess(IOperation memberReference)
    {
        var current = memberReference;
        var parent = memberReference.Parent;

        while (parent is IConversionOperation or IParenthesizedOperation)
        {
            current = parent;
            parent = parent.Parent;
        }

        if (parent is INameOfOperation)
        {
            return (false, false);
        }

        if (parent is ISimpleAssignmentOperation simpleAssignment && IsTargetOperation(simpleAssignment.Target, current))
        {
            return (false, true);
        }

        if (parent is ICompoundAssignmentOperation compoundAssignment && IsTargetOperation(compoundAssignment.Target, current))
        {
            return (true, true);
        }

        if (parent is IIncrementOrDecrementOperation incrementOrDecrement && IsTargetOperation(incrementOrDecrement.Target, current))
        {
            return (true, true);
        }

        if (parent is IArgumentOperation argument && IsTargetOperation(argument.Value, current))
        {
            return argument.Parameter?.RefKind switch
            {
                RefKind.Out => (false, true),
                RefKind.Ref => (true, true),
                _ => (true, false),
            };
        }

        return (true, false);
    }

    private static bool IsTargetOperation(IOperation target, IOperation candidate)
    {
        var unwrappedTarget = UnwrapNonSemanticOperation(target);
        var unwrappedCandidate = UnwrapNonSemanticOperation(candidate);

        return ReferenceEquals(unwrappedTarget, unwrappedCandidate)
               || (
                   ReferenceEquals(unwrappedTarget.Syntax.SyntaxTree, unwrappedCandidate.Syntax.SyntaxTree)
                   && unwrappedTarget.Syntax.Span.Equals(unwrappedCandidate.Syntax.Span));
    }

    private static IOperation UnwrapNonSemanticOperation(IOperation operation)
    {
        var current = operation;
        while (current is IConversionOperation or IParenthesizedOperation)
        {
            var previous = current;
            current = current.ChildOperations.FirstOrDefault() ?? current;
            if (ReferenceEquals(current, previous))
            {
                break;
            }
        }

        return current;
    }

    private static EntityId? ResolveInternalMemberId(
        ISymbol symbol,
        IReadOnlyDictionary<MemberDeclarationLocationKey, EntityId> memberIdByDeclarationLocation)
    {
        foreach (var location in symbol.Locations.Where(static x => x.IsInSource))
        {
            var lineSpan = location.GetLineSpan();
            var key = new MemberDeclarationLocationKey(
                location.SourceTree?.FilePath ?? string.Empty,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1);

            if (memberIdByDeclarationLocation.TryGetValue(key, out var memberId))
            {
                return memberId;
            }
        }

        return null;
    }

    private static IReadOnlyList<MetadataReference> BuildCompilationReferences(List<IngestionDiagnostic> diagnostics)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            diagnostics.Add(new IngestionDiagnostic(
                "method:call:references:missing",
                "Trusted platform assemblies were unavailable; semantic call resolution may be degraded."));
            return [];
        }

        return tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static EntityId ResolveSourceMethodId(
        Location declarationLocation,
        IReadOnlyDictionary<MethodDeclarationLocationKey, EntityId> methodIdByDeclarationLocation)
    {
        var lineSpan = declarationLocation.GetLineSpan();
        var key = new MethodDeclarationLocationKey(
            declarationLocation.SourceTree?.FilePath ?? string.Empty,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);

        return methodIdByDeclarationLocation.TryGetValue(key, out var methodId)
            ? methodId
            : default;
    }

    private static EntityId? ResolveTargetMethodId(
        IMethodSymbol targetSymbol,
        IReadOnlyList<MethodRelationshipCandidate> candidates)
    {
        var targetName = targetSymbol.MethodKind == MethodKind.Constructor
            ? ".ctor"
            : targetSymbol.Name;
        var arity = targetSymbol.Arity;
        var parameterCount = targetSymbol.Parameters.Length;

        var nameAndShapeMatches = candidates
            .Where(x =>
                string.Equals(x.Kind, "method", StringComparison.Ordinal)
                && ExtractMethodNameForRelationship(x.CanonicalName).Equals(targetName, StringComparison.Ordinal)
                && x.Arity == arity
                && x.ParameterTypeSignatures.Count == parameterCount)
            .ToArray();

        if (nameAndShapeMatches.Length == 1)
        {
            return nameAndShapeMatches[0].MethodId;
        }

        if (nameAndShapeMatches.Length == 0)
        {
            return null;
        }

        var symbolParameterSignatures = targetSymbol.Parameters
            .Select(x => NormalizeMethodIdentityTypeSignature(x.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))
            .ToArray();

        var exactMatches = nameAndShapeMatches
            .Where(candidate => candidate.ParameterTypeSignatures.SequenceEqual(symbolParameterSignatures, StringComparer.Ordinal))
            .ToArray();

        return exactMatches.Length == 1
            ? exactMatches[0].MethodId
            : null;
    }

    private static string GetTypeQualifiedName(ITypeSymbol typeSymbol)
    {
        var segments = new Stack<string>();
        var currentType = typeSymbol;
        while (currentType is not null)
        {
            segments.Push(currentType.MetadataName);
            currentType = currentType.ContainingType;
        }

        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString();
        if (!string.IsNullOrWhiteSpace(namespaceName) && !typeSymbol.ContainingNamespace!.IsGlobalNamespace)
        {
            segments.Push(namespaceName);
        }

        return string.Join(".", segments);
    }

    private EntityId GetOrCreateUnresolvedCallTargetId(
        string targetText,
        string resolutionReason,
        Dictionary<string, EntityId> unresolvedCallTargetIdByText,
        List<SemanticTriple> triples)
    {
        lock (_unresolvedCallTargetGate)
        {
            var key = $"{resolutionReason}|{targetText}";
            if (unresolvedCallTargetIdByText.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var unresolvedNaturalKey = $"{resolutionReason}:{targetText}";
            var unresolvedId = _stableIdGenerator.Create(new EntityKey("unresolved-call-target", unresolvedNaturalKey));
            unresolvedCallTargetIdByText[key] = unresolvedId;
            AddEntityTriples(
                triples,
                unresolvedId,
                "unresolved-call-target",
                targetText,
                $"unresolved-call/{targetText}");
            triples.Add(new SemanticTriple(
                new EntityNode(unresolvedId),
                CorePredicates.ResolutionReason,
                new LiteralNode(resolutionReason)));

            return unresolvedId;
        }
    }

    private static string FormatDeclarationSourceLocation(DeclarationSourceLocation location)
    {
        return $"{location.RelativeFilePath}|{location.Line}|{location.Column}";
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
        lock (_externalStubGate)
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
    }

    private EntityId GetOrCreateUnresolvedReferenceId(
        string referenceName,
        string resolutionReason,
        Dictionary<string, EntityId> unresolvedReferenceIdByText,
        List<SemanticTriple> triples)
    {
        var key = $"{resolutionReason}|{referenceName}";
        if (unresolvedReferenceIdByText.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var unresolvedNaturalKey = $"{resolutionReason}:{referenceName}";
        var unresolvedId = _stableIdGenerator.Create(new EntityKey("unresolved-type-reference", unresolvedNaturalKey));
        unresolvedReferenceIdByText[key] = unresolvedId;

        AddEntityTriples(
            triples,
            unresolvedId,
            "unresolved-type-reference",
            referenceName,
            $"unresolved/{referenceName}");
        triples.Add(new SemanticTriple(
            new EntityNode(unresolvedId),
            CorePredicates.ResolutionReason,
            new LiteralNode(resolutionReason)));

        return unresolvedId;
    }

    private static bool IsExternalStubCandidate(string normalizedReferenceName)
    {
        if (string.IsNullOrWhiteSpace(normalizedReferenceName))
        {
            return false;
        }

        if (!normalizedReferenceName.All(c => char.IsLetterOrDigit(c) || c is '_' or '.'))
        {
            return false;
        }

        if (normalizedReferenceName.Contains('.', StringComparison.Ordinal))
        {
            return true;
        }

        return IsKnownSimpleExternalTypeName(normalizedReferenceName);
    }

    private static bool IsKnownSimpleExternalTypeName(string normalizedReferenceName)
    {
        return normalizedReferenceName switch
        {
            "object" or "string" or "bool" or "byte" or "sbyte" or "short" or "ushort"
                or "int" or "uint" or "long" or "ulong" or "nint" or "nuint"
                or "float" or "double" or "decimal" or "char" or "void" or "dynamic"
                or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Guid" or "Uri"
                or "Task" or "ValueTask" or "CancellationToken"
                or "IEnumerable" or "IReadOnlyList" or "IReadOnlyDictionary"
                or "IDisposable" or "IAsyncDisposable"
                or "List" or "Dictionary" or "HashSet" => true,
            _ => false,
        };
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

        while (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            normalized = normalized[..^2].TrimEnd();
        }

        while (normalized.EndsWith("?", StringComparison.Ordinal)
               || normalized.EndsWith("!", StringComparison.Ordinal))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized.Trim();
    }

    private static string CreateMethodGroupKey(MethodDiscoveryNode method)
    {
        var parameterTypes = method.Parameters
            .OrderBy(x => x.Ordinal)
            .Select(x => NormalizeMethodIdentityTypeSignature(x.DeclaredTypeName ?? string.Empty))
            .ToArray();

        return $"{method.Kind}:{method.CanonicalName}:{method.Arity}:{string.Join("|", parameterTypes)}";
    }

    private static string BuildMethodPath(string qualifiedTypeName, MethodDiscoveryNode method)
    {
        var parameterTypes = method.Parameters
            .OrderBy(x => x.Ordinal)
            .Select(x => NormalizeMethodIdentityTypeSignature(x.DeclaredTypeName ?? string.Empty))
            .ToArray();

        var canonicalNameWithArity = method.Arity > 0
            ? $"{method.CanonicalName}`{method.Arity}"
            : method.CanonicalName;

        var signature = parameterTypes.Length == 0
            ? $"{canonicalNameWithArity}()"
            : $"{canonicalNameWithArity}({string.Join(", ", parameterTypes)})";

        return $"{qualifiedTypeName}.{signature}";
    }

    private static IReadOnlyList<ProjectAssemblyContext> BuildProjectAssemblyContexts(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> projectAssemblyNameByPath)
    {
        return projectAssemblyNameByPath
            .Select(x =>
            {
                var projectDirectory = Path.GetDirectoryName(x.Key) ?? string.Empty;
                var relativeDirectory = ToRelativePath(repositoryRoot, projectDirectory);
                return new ProjectAssemblyContext(relativeDirectory, x.Value);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.RelativeDirectory))
            .OrderByDescending(x => x.RelativeDirectory.Length)
            .ThenBy(x => x.RelativeDirectory, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveAssemblyNameForSourceFile(
        string relativeSourceFilePath,
        IReadOnlyList<ProjectAssemblyContext> projectAssemblyContexts)
    {
        foreach (var projectContext in projectAssemblyContexts)
        {
            var projectDirectory = projectContext.RelativeDirectory;
            if (relativeSourceFilePath.Equals(projectDirectory, StringComparison.Ordinal) ||
                relativeSourceFilePath.StartsWith(projectDirectory + "/", StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(projectContext.AssemblyName)
                    ? "unknown-assembly"
                    : projectContext.AssemblyName;
            }
        }

        return "unknown-assembly";
    }

    private static string NormalizeMethodIdentityTypeSignature(string typeSignature)
    {
        var normalized = typeSignature.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized[8..];
        }

        return new string(normalized.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    private static IReadOnlyList<EntityId> EnumerateBaseTypeIdsInOrder(
        EntityId sourceTypeId,
        IReadOnlyDictionary<EntityId, List<EntityId>> directBaseTypeIdsByTypeId)
    {
        var ordered = new List<EntityId>();
        var seen = new HashSet<EntityId>();
        var pending = new Queue<EntityId>();

        if (directBaseTypeIdsByTypeId.TryGetValue(sourceTypeId, out var directBases))
        {
            foreach (var directBase in directBases)
            {
                pending.Enqueue(directBase);
            }
        }

        while (pending.Count > 0)
        {
            var next = pending.Dequeue();
            if (!seen.Add(next))
            {
                continue;
            }

            ordered.Add(next);

            if (directBaseTypeIdsByTypeId.TryGetValue(next, out var inheritedBases))
            {
                foreach (var inheritedBase in inheritedBases)
                {
                    pending.Enqueue(inheritedBase);
                }
            }
        }

        return ordered;
    }

    private static IReadOnlyList<EntityId> GetImplementedInterfaceTypeIds(
        EntityId sourceTypeId,
        IReadOnlyDictionary<EntityId, List<EntityId>> directBaseTypeIdsByTypeId,
        IReadOnlyDictionary<EntityId, List<EntityId>> directInterfaceTypeIdsByTypeId)
    {
        var collected = new HashSet<EntityId>();
        var pendingInterfaces = new Queue<EntityId>();

        if (directInterfaceTypeIdsByTypeId.TryGetValue(sourceTypeId, out var directInterfaces))
        {
            foreach (var directInterface in directInterfaces)
            {
                pendingInterfaces.Enqueue(directInterface);
            }
        }

        foreach (var baseTypeId in EnumerateBaseTypeIdsInOrder(sourceTypeId, directBaseTypeIdsByTypeId))
        {
            if (!directInterfaceTypeIdsByTypeId.TryGetValue(baseTypeId, out var baseInterfaces))
            {
                continue;
            }

            foreach (var baseInterface in baseInterfaces)
            {
                pendingInterfaces.Enqueue(baseInterface);
            }
        }

        while (pendingInterfaces.Count > 0)
        {
            var interfaceTypeId = pendingInterfaces.Dequeue();
            if (!collected.Add(interfaceTypeId))
            {
                continue;
            }

            if (!directBaseTypeIdsByTypeId.TryGetValue(interfaceTypeId, out var parentInterfaces))
            {
                continue;
            }

            foreach (var parentInterface in parentInterfaces)
            {
                pendingInterfaces.Enqueue(parentInterface);
            }
        }

        return collected
            .OrderBy(x => x.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateMethodRelationshipMatchKey(
        string methodName,
        int arity,
        IReadOnlyList<string> parameterTypeSignatures)
    {
        return $"{methodName}:{arity}:{string.Join("|", parameterTypeSignatures)}";
    }

    private static string ExtractMethodNameForRelationship(string canonicalName)
    {
        var lastDot = canonicalName.LastIndexOf('.');
        return lastDot >= 0
            ? canonicalName[(lastDot + 1)..]
            : canonicalName;
    }

    private static string? ExtractExplicitInterfaceQualifier(string canonicalName)
    {
        var lastDot = canonicalName.LastIndexOf('.');
        return lastDot > 0
            ? canonicalName[..lastDot]
            : null;
    }

    private static bool InterfaceQualifierMatches(
        string explicitQualifier,
        EntityId interfaceTypeId,
        IReadOnlyDictionary<EntityId, string> typeQualifiedNameById)
    {
        if (!typeQualifiedNameById.TryGetValue(interfaceTypeId, out var interfaceQualifiedName))
        {
            return false;
        }

        var trimmedQualifier = explicitQualifier.Trim();
        if (trimmedQualifier.StartsWith("global::", StringComparison.Ordinal))
        {
            trimmedQualifier = trimmedQualifier[8..];
        }

        if (interfaceQualifiedName.Equals(trimmedQualifier, StringComparison.Ordinal))
        {
            return true;
        }

        var simpleName = interfaceQualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return !string.IsNullOrWhiteSpace(simpleName)
               && simpleName.Equals(trimmedQualifier, StringComparison.Ordinal);
    }

    private sealed record PendingMemberTypeLink(
        EntityId MemberId,
        string NamespaceName,
        string DeclaredTypeName,
        IReadOnlyList<string> ImportedNamespaces,
        IReadOnlyDictionary<string, string> ImportedAliases);

    private sealed record PendingMethodReturnTypeLink(
        EntityId MethodId,
        string NamespaceName,
        string ReturnTypeName,
        IReadOnlyList<string> ImportedNamespaces,
        IReadOnlyDictionary<string, string> ImportedAliases);

    private sealed record PendingMethodExtendedTypeLink(
        EntityId MethodId,
        string NamespaceName,
        string ExtendedTypeName,
        IReadOnlyList<string> ImportedNamespaces,
        IReadOnlyDictionary<string, string> ImportedAliases);

    private sealed record PendingMethodParameterTypeLink(
        EntityId ParameterId,
        EntityId MethodId,
        string NamespaceName,
        string DeclaredTypeName,
        IReadOnlyList<string> ImportedNamespaces,
        IReadOnlyDictionary<string, string> ImportedAliases);

    private sealed record ControllerEndpointCandidate(
        string HttpMethod,
        string AuthoredRouteText,
        string NormalizedRouteKey,
        string Confidence);

    private sealed record MemberDeclarationLocationKey(string RelativeFilePath, int Line, int Column);

    private sealed record MethodDeclarationLocationKey(string RelativeFilePath, int Line, int Column);

    private sealed record ProjectAssemblyContext(string RelativeDirectory, string AssemblyName);

    private sealed record MethodRelationshipCandidate(
        EntityId MethodId,
        string Kind,
        string CanonicalName,
        int Arity,
        IReadOnlyList<string> ParameterTypeSignatures,
        bool IsOverride);

    private sealed record HalsteadMetricSnapshot(
        int DistinctOperators,
        int DistinctOperands,
        int TotalOperators,
        int TotalOperands,
        int Vocabulary,
        int Length,
        double Volume,
        double Difficulty,
        double Effort,
        double EstimatedBugs,
        double EstimatedTimeSeconds);

    private sealed record LocMetricSnapshot(
        int TotalLines,
        int CodeLines,
        int CommentLines,
        int BlankLines);

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

    private static Dictionary<string, string> BuildHeadSourceSnapshot(
        string repositoryRoot,
        IReadOnlyList<string> relativeSourcePaths,
        List<IngestionDiagnostic> diagnostics)
    {
        var sourceTextByRelativePath = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var relativePath in relativeSourcePaths
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var gitPath = relativePath.Replace('\\', '/');
            var result = RunGitText(repositoryRoot, "show", $"HEAD:{gitPath}");
            if (result.ExitCode != 0)
            {
                diagnostics.Add(new IngestionDiagnostic(
                    "git:head:file:not-available",
                    $"Failed to load HEAD content for '{relativePath}'; falling back to working tree file content when available."));
                continue;
            }

            sourceTextByRelativePath[relativePath] = result.Output;
        }

        return sourceTextByRelativePath;
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

    private sealed record SemanticCallProjectBatch(
        string ProjectSortKey,
        IReadOnlyList<string> SourceFiles);

    private sealed record SemanticCallProjectBatchResult(
        string ProjectSortKey,
        IReadOnlyList<SemanticTriple> Triples,
        IReadOnlyList<IngestionDiagnostic> Diagnostics,
        IReadOnlyDictionary<EntityId, HashSet<EntityId>> DeclarationDependenciesByTypeId,
        IReadOnlyDictionary<EntityId, HashSet<EntityId>> MethodBodyDependenciesByTypeId);

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
