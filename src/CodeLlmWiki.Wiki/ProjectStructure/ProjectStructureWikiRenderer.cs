using System.Globalization;
using System.Text;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Wiki.ProjectStructure;

public sealed class ProjectStructureWikiRenderer : IProjectStructureWikiRenderer
{
    public IReadOnlyList<WikiPage> Render(ProjectStructureWikiModel model, int? maxMergeEntriesPerFile = null)
    {
        var resolver = new WikiPathResolver();
        resolver.RegisterRepository(model.Repository);

        var orderedSolutions = model.Solutions.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        foreach (var solution in orderedSolutions)
        {
            resolver.RegisterSolution(solution);
        }

        var orderedProjects = model.Projects.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        foreach (var project in orderedProjects)
        {
            resolver.RegisterProject(project);
        }

        var orderedPackages = model.Packages.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
        foreach (var package in orderedPackages)
        {
            resolver.RegisterPackage(package);
        }

        var orderedNamespaces = model.Declarations.Namespaces
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.Name,
                    x.Name,
                    x.Path,
                    x.Id.Value),
                StringComparer.Ordinal)
            .ToArray();

        foreach (var namespaceDeclaration in orderedNamespaces)
        {
            resolver.RegisterNamespace(namespaceDeclaration);
        }

        var orderedTypes = model.Declarations.Types
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.NamespaceId is null
                        ? "<global>"
                        : model.Declarations.Namespaces.FirstOrDefault(n => n.Id == x.NamespaceId)?.Name ?? string.Empty,
                    x.Name,
                    x.Path,
                    x.Id.Value),
                StringComparer.Ordinal)
            .ToArray();

        foreach (var typeDeclaration in orderedTypes)
        {
            resolver.RegisterType(typeDeclaration);
        }

        var typeById = model.Declarations.Types.ToDictionary(x => x.Id, x => x);
        var orderedMethods = model.Declarations.Methods.Declarations
            .OrderBy(x => typeById.TryGetValue(x.DeclaringTypeId, out var declaringType) ? declaringType.Path : string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Signature, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (var methodDeclaration in orderedMethods)
        {
            if (!typeById.TryGetValue(methodDeclaration.DeclaringTypeId, out var declaringType))
            {
                continue;
            }

            resolver.RegisterMethod(methodDeclaration, declaringType);
        }

        var orderedFiles = model.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        foreach (var file in orderedFiles)
        {
            resolver.RegisterFile(file);
        }

        var orderedEndpointGroups = model.Endpoints.Groups
            .OrderBy(x => x.Family, StringComparer.Ordinal)
            .ThenBy(x => x.CanonicalKey, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (var endpointGroup in orderedEndpointGroups)
        {
            resolver.RegisterEndpointGroup(endpointGroup);
        }

        var orderedEndpoints = model.Endpoints.Endpoints
            .OrderBy(x => x.Family, StringComparer.Ordinal)
            .ThenBy(x => x.NormalizedRouteKey, StringComparer.Ordinal)
            .ThenBy(x => x.HttpMethod, StringComparer.Ordinal)
            .ThenBy(x => x.CanonicalSignature, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (var endpoint in orderedEndpoints)
        {
            resolver.RegisterEndpoint(endpoint);
        }

        var indexPath = resolver.RegisterIndex();
        var packageById = model.Packages.ToDictionary(x => x.Id, x => x);
        var projectById = model.Projects.ToDictionary(x => x.Id, x => x);
        var namespaceById = model.Declarations.Namespaces.ToDictionary(x => x.Id, x => x);
        var namespaceMetricRollupByNamespaceId = model.StructuralMetrics.Namespaces
            .GroupBy(x => x.NamespaceId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderByDescending(v => v.Recursive.Coverage.AnalyzableMethods)
                    .ThenBy(v => v.Path, StringComparer.Ordinal)
                    .First());
        var memberById = model.Declarations.Members.ToDictionary(x => x.Id, x => x);
        var methodById = orderedMethods.ToDictionary(x => x.Id, x => x);
        var endpointGroupById = orderedEndpointGroups.ToDictionary(x => x.Id, x => x);
        var endpointById = orderedEndpoints.ToDictionary(x => x.Id, x => x);
        var endpointIdsByDeclaringTypeId = orderedEndpoints
            .Where(x => x.DeclaringTypeId is not null)
            .GroupBy(x => x.DeclaringTypeId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.Id)
                    .Distinct()
                    .OrderBy(v => endpointById[v].Family, StringComparer.Ordinal)
                    .ThenBy(v => endpointById[v].NormalizedRouteKey, StringComparer.Ordinal)
                    .ThenBy(v => endpointById[v].HttpMethod, StringComparer.Ordinal)
                    .ThenBy(v => endpointById[v].CanonicalSignature, StringComparer.Ordinal)
                    .ToArray());
        var endpointIdsByDeclaringMethodId = orderedEndpoints
            .Where(x => x.DeclaringMethodId is not null)
            .GroupBy(x => x.DeclaringMethodId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.Id)
                    .Distinct()
                    .OrderBy(v => endpointById[v].Family, StringComparer.Ordinal)
                    .ThenBy(v => endpointById[v].NormalizedRouteKey, StringComparer.Ordinal)
                    .ThenBy(v => endpointById[v].HttpMethod, StringComparer.Ordinal)
                    .ThenBy(v => endpointById[v].CanonicalSignature, StringComparer.Ordinal)
                    .ToArray());
        var implementedMethodIdsBySourceMethodId = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.ImplementsMethod && x.TargetMethodId is not null)
            .GroupBy(x => x.SourceMethodId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.TargetMethodId!.Value)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var overriddenMethodIdsBySourceMethodId = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.OverridesMethod && x.TargetMethodId is not null)
            .GroupBy(x => x.SourceMethodId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.TargetMethodId!.Value)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var callRelationsBySourceMethodId = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.Calls)
            .GroupBy(x => x.SourceMethodId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<MethodRelationNode>)x
                    .OrderBy(v => v.TargetMethodId?.Value ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(v => v.ExternalTargetType?.DisplayText ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(v => v.ExternalAssemblyName ?? string.Empty, StringComparer.Ordinal)
                    .ToArray());
        var calledByMethodIdsByTargetMethodId = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.Calls && x.TargetMethodId is not null)
            .GroupBy(x => x.TargetMethodId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.SourceMethodId)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var readMethodIdsByTargetMemberId = model.Declarations.Methods.Relations
            .Where(x =>
                (x.Kind == MethodRelationKind.ReadsProperty || x.Kind == MethodRelationKind.ReadsField)
                && x.TargetMemberId is not null)
            .GroupBy(x => x.TargetMemberId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.SourceMethodId)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var writeMethodIdsByTargetMemberId = model.Declarations.Methods.Relations
            .Where(x =>
                (x.Kind == MethodRelationKind.WritesProperty || x.Kind == MethodRelationKind.WritesField)
                && x.TargetMemberId is not null)
            .GroupBy(x => x.TargetMemberId!.Value)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.SourceMethodId)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var readMemberIdsBySourceMethodId = model.Declarations.Methods.Relations
            .Where(x =>
                (x.Kind == MethodRelationKind.ReadsProperty || x.Kind == MethodRelationKind.ReadsField)
                && x.TargetMemberId is not null)
            .GroupBy(x => x.SourceMethodId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.TargetMemberId!.Value)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var writeMemberIdsBySourceMethodId = model.Declarations.Methods.Relations
            .Where(x =>
                (x.Kind == MethodRelationKind.WritesProperty || x.Kind == MethodRelationKind.WritesField)
                && x.TargetMemberId is not null)
            .GroupBy(x => x.SourceMethodId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.TargetMemberId!.Value)
                    .Distinct()
                    .OrderBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
        var fileById = model.Files.ToDictionary(x => x.Id, x => x);
        var namespaceBacklinksByFileId = BuildNamespaceBacklinksByFileId(model.Declarations.Namespaces, fileById);
        var typeBacklinksByFileId = BuildTypeBacklinksByFileId(model.Declarations.Types, fileById);
        var memberBacklinksByFileId = BuildMemberBacklinksByFileId(model.Declarations.Members, fileById);
        var methodBacklinksByFileId = BuildMethodBacklinksByFileId(orderedMethods, fileById);
        var inheritedByTypeIdsByBaseTypeId = BuildReverseTypeRelationshipMap(
            model.Declarations.Types,
            type => type.DirectBaseTypes
                .Where(x => x.TypeId is not null)
                .Select(x => x.TypeId!.Value),
            typeById);
        var implementedByTypeIdsByInterfaceTypeId = BuildReverseTypeRelationshipMap(
            model.Declarations.Types,
            type => type.DirectInterfaceTypes
                .Where(x => x.TypeId is not null)
                .Select(x => x.TypeId!.Value),
            typeById);
        var externalCallTargetsByPackageId = model.Declarations.Methods.Relations
            .Where(x => x.Kind == MethodRelationKind.Calls && x.ExternalTargetType is not null)
            .Select(relation => TryResolveExternalTargetPackage(relation, orderedPackages, out var package)
                ? new
                {
                    package.Id,
                    TargetDisplayText = relation.ExternalTargetType!.DisplayText,
                }
                : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => x.Id)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<string>)x
                    .Select(v => v.TargetDisplayText)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToArray());
        var inheritedPackageTypeUsageByPackageId = BuildInheritedPackageTypeUsageByPackageId(model.Declarations.Types, orderedPackages);

        var pages = new List<WikiPage>
        {
            RenderRepositoryPage(model, resolver),
        };
        pages.AddRange(RenderGuidancePages(model, resolver));

        pages.AddRange(orderedSolutions.Select(solution => RenderSolutionPage(model.Repository.Id.Value, solution, projectById, resolver)));
        pages.AddRange(orderedProjects.Select(project => RenderProjectPage(model.Repository.Id.Value, project, packageById, resolver)));
        pages.AddRange(orderedPackages.Select(package =>
            RenderPackagePage(
                model.Repository.Id.Value,
                package,
                inheritedPackageTypeUsageByPackageId,
                externalCallTargetsByPackageId,
                resolver)));
        if (model.DependencyAttribution.DeclarationUnknown.UsageCount > 0
            || model.DependencyAttribution.MethodBodyUnknown.UsageCount > 0)
        {
            pages.Add(RenderUnknownPackageAttributionPage(model.Repository.Id.Value, model.DependencyAttribution, methodById, resolver));
        }
        pages.AddRange(orderedNamespaces.Select(namespaceDeclaration =>
            RenderNamespacePage(
                model.Repository.Id.Value,
                namespaceDeclaration,
                namespaceById,
                typeById,
                namespaceMetricRollupByNamespaceId,
                resolver)));
        pages.AddRange(orderedTypes.Select(typeDeclaration =>
            RenderTypePage(
                model.Repository.Id.Value,
                typeDeclaration,
                namespaceById,
                typeById,
                memberById,
                methodById,
                readMethodIdsByTargetMemberId,
                writeMethodIdsByTargetMemberId,
                inheritedByTypeIdsByBaseTypeId,
                implementedByTypeIdsByInterfaceTypeId,
                endpointIdsByDeclaringTypeId,
                endpointById,
                fileById,
                orderedProjects,
                resolver)));
        pages.AddRange(orderedMethods.Select(method =>
            RenderMethodPage(
                model.Repository.Id.Value,
                method,
                orderedPackages,
                typeById,
                methodById,
                memberById,
                implementedMethodIdsBySourceMethodId,
                overriddenMethodIdsBySourceMethodId,
                callRelationsBySourceMethodId,
                readMemberIdsBySourceMethodId,
                writeMemberIdsBySourceMethodId,
                calledByMethodIdsByTargetMethodId,
                endpointIdsByDeclaringMethodId,
                endpointById,
                fileById,
                resolver)));
        pages.AddRange(orderedEndpointGroups.Select(endpointGroup =>
            RenderEndpointGroupPage(
                model.Repository.Id.Value,
                endpointGroup,
                namespaceById,
                typeById,
                endpointById,
                fileById,
                resolver)));
        pages.AddRange(orderedEndpoints.Select(endpoint =>
            RenderEndpointPage(
                model.Repository.Id.Value,
                endpoint,
                namespaceById,
                typeById,
                methodById,
                endpointGroupById,
                fileById,
                resolver)));
        pages.AddRange(orderedFiles.Select(file => RenderFilePage(model.Repository, file, namespaceBacklinksByFileId, typeBacklinksByFileId, memberBacklinksByFileId, methodBacklinksByFileId, resolver, maxMergeEntriesPerFile)));
        pages.AddRange(RenderHotspotPages(model, resolver));

        pages.Add(RenderIndexPage(model, resolver, indexPath));

        return pages
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static WikiPage RenderRepositoryPage(ProjectStructureWikiModel model, WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Repository: {model.Repository.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{model.Repository.Path}`");
        sb.AppendLine($"- Branch Snapshot: `{model.Repository.HeadBranch}`");
        sb.AppendLine($"- Mainline Branch: `{model.Repository.MainlineBranch}`");
        sb.AppendLine($"- Solutions: {model.Solutions.Count}");
        sb.AppendLine($"- Projects: {model.Projects.Count}");
        sb.AppendLine($"- Packages: {model.Packages.Count}");
        sb.AppendLine($"- Namespaces: {model.Declarations.Namespaces.Count}");
        sb.AppendLine($"- Types: {model.Declarations.Types.Count}");
        sb.AppendLine($"- Methods: {model.Declarations.Methods.Declarations.Count}");
        sb.AppendLine($"- Endpoint Groups: {model.Endpoints.Groups.Count}");
        sb.AppendLine($"- Endpoints: {model.Endpoints.Endpoints.Count}");
        sb.AppendLine($"- Files: {model.Files.Count}");
        sb.AppendLine($"- Submodules: {model.Submodules.Count}");
        sb.AppendLine($"- Index: {ToWikiLink("index/repository-index", "Repository Index")}");
        sb.AppendLine();
        sb.AppendLine("## Guidance");
        sb.AppendLine($"- {resolver.ToMarkdownLink("guidance/human.md", "Human Guide")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("guidance/llm-contract.md", "LLM Contract")}");
        sb.AppendLine();
        sb.AppendLine("## Hotspots");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/methods.md", "Methods")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/types.md", "Types")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/files.md", "Files")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/namespaces.md", "Namespaces")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/projects.md", "Projects")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/repository.md", "Repository")}");
        sb.AppendLine();
        sb.AppendLine("## Endpoints");
        sb.AppendLine($"- {resolver.ToMarkdownLink("index/repository-index.md", "Endpoint Groups", "endpoint-groups")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("index/repository-index.md", "Endpoints", "endpoints")}");
        sb.AppendLine();
        sb.AppendLine("## Solutions");

        foreach (var solution in model.Solutions.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {resolver.ToWikiLink(solution.Id, solution.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Namespaces");

        foreach (var namespaceDeclaration in model.Declarations.Namespaces
                     .OrderBy(x => x.Path, StringComparer.Ordinal)
                     .ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {resolver.ToWikiLink(namespaceDeclaration.Id, namespaceDeclaration.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Types");

        foreach (var typeDeclaration in model.Declarations.Types
                     .OrderBy(x => x.Path, StringComparer.Ordinal)
                     .ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {resolver.ToWikiLink(typeDeclaration.Id, typeDeclaration.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Opaque Submodule Dependencies");

        foreach (var submodule in model.Submodules.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{submodule.Path}` ({submodule.Url})");
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(model.Repository.Id),
            Title: model.Repository.Name,
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", model.Repository.Id.Value),
                    KeyValue("entity_type", "repository"),
                    KeyValue("repository_id", model.Repository.Id.Value),
                    KeyValue("repository_name", model.Repository.Name),
                    KeyValue("repository_path", model.Repository.Path),
                    KeyValue("head_branch", model.Repository.HeadBranch),
                    KeyValue("mainline_branch", model.Repository.MainlineBranch),
                ],
                sb.ToString().TrimEnd()));
    }

    private static IReadOnlyList<WikiPage> RenderGuidancePages(ProjectStructureWikiModel model, WikiPathResolver resolver)
    {
        return
        [
            RenderHumanGuidancePage(model.Repository.Id.Value, model.Repository.HeadBranch, resolver),
            RenderLlmContractGuidancePage(model.Repository.Id.Value, model.Repository.HeadBranch, resolver),
        ];
    }

    private static WikiPage RenderHumanGuidancePage(
        string repositoryId,
        string headBranch,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Human Guide");
        sb.AppendLine();
        sb.AppendLine("Use this page to orient quickly in the generated wiki.");
        sb.AppendLine();
        sb.AppendLine("## Start Here");
        sb.AppendLine($"- {resolver.ToMarkdownLink("index/repository-index.md", "Repository Index")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("hotspots/repository.md", "Repository Hotspots")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("guidance/llm-contract.md", "LLM Contract")}");

        return new WikiPage(
            RelativePath: "guidance/human.md",
            Title: "Human Guide",
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", $"guidance:human:{repositoryId}"),
                    KeyValue("entity_type", "guidance"),
                    KeyValue("repository_id", repositoryId),
                    KeyValue("guidance_kind", "human"),
                    KeyValue("head_branch", headBranch),
                ],
                sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderLlmContractGuidancePage(
        string repositoryId,
        string headBranch,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# LLM Contract");
        sb.AppendLine();
        sb.AppendLine("Normative operating guidance for LLM agents over this snapshot.");
        sb.AppendLine();
        sb.AppendLine("## Start Here");
        sb.AppendLine($"- {resolver.ToMarkdownLink("index/repository-index.md", "Repository Index")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("guidance/human.md", "Human Guide")}");
        sb.AppendLine();
        sb.AppendLine("## Contract Rules");
        sb.AppendLine("- Agents MUST navigate via published wiki links before making material claims.");
        sb.AppendLine("- Agents MUST prioritize human-readable output and keep IDs out of narrative body content.");
        sb.AppendLine("- Agents SHOULD keep responses concise and link-first.");
        sb.AppendLine("- Agents SHOULD use deterministic section ordering to aid comparison across runs.");
        sb.AppendLine();
        sb.AppendLine("## Response Template");
        sb.AppendLine("- Summary");
        sb.AppendLine("- Evidence Links");
        sb.AppendLine("- Gaps/Risks");
        sb.AppendLine("- Next Queries");
        sb.AppendLine();
        sb.AppendLine("## Link Policy");
        sb.AppendLine("- Use wiki links for internal page references in prose.");
        sb.AppendLine("- Use markdown links for deep anchors and links inside table cells.");
        sb.AppendLine();
        sb.AppendLine("## Evidence Policy");
        sb.AppendLine("- Material claims MUST include one or more supporting wiki links.");
        sb.AppendLine("- Uncertain or missing evidence MUST be reported in `Gaps/Risks`.");
        sb.AppendLine();
        sb.AppendLine("## Guardrails");
        sb.AppendLine("- Do not claim capabilities not present in this repository snapshot.");
        sb.AppendLine("- Do not claim cross-repository tracing in single-repository mode.");
        sb.AppendLine("- Do not expose internal IDs in narrative output.");
        sb.AppendLine();
        sb.AppendLine("## Named Recipes");
        sb.AppendLine("<a id=\"recipe-structure-survey\"></a>");
        sb.AppendLine("### Structure Survey");
        sb.AppendLine("1. Start from Repository Index.");
        sb.AppendLine("2. Traverse solutions, projects, namespaces, and types via links.");
        sb.AppendLine("3. Report findings using the required response template.");
        sb.AppendLine();
        sb.AppendLine("<a id=\"recipe-hotspot-triage\"></a>");
        sb.AppendLine("### Hotspot Triage");
        sb.AppendLine("1. Start from repository hotspot pages.");
        sb.AppendLine("2. Pivot into linked type and method pages.");
        sb.AppendLine("3. Report top risks with evidence links.");
        sb.AppendLine();
        sb.AppendLine("<a id=\"recipe-dependency-trace\"></a>");
        sb.AppendLine("### Dependency Trace");
        sb.AppendLine("1. Start from package pages and external type anchors.");
        sb.AppendLine("2. Pivot from package usage to linked internal types and methods.");
        sb.AppendLine("3. Call out unknown attribution in `Gaps/Risks`.");
        sb.AppendLine();
        sb.AppendLine("## Capability Matrix");
        sb.AppendLine("| backlog_item | status | reference |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-001", "bl-001-repository-structure-ingestion", headBranch)} | available | repository structure ingestion |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-008", "bl-008-method-declarations", headBranch)} | available | method declarations and pages |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-011", "bl-011-dependency-usage-mapping", headBranch)} | available | dependency usage mapping |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-013", "bl-013-domain-term-extraction-and-linking", headBranch)} | deferred | domain term extraction and linking |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-014", "bl-014-endpoint-discovery-and-behavior-metadata", headBranch)} | deferred | endpoint discovery and metadata |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-016", "bl-016-cross-system-dependency-tracing", headBranch)} | deferred | cross-system dependency tracing |");
        sb.AppendLine($"| {BuildBacklogItemMarkdownLink("BL-018", "bl-018-multi-language-analyzer-expansion", headBranch)} | deferred | multi-language analyzer expansion |");

        return new WikiPage(
            RelativePath: "guidance/llm-contract.md",
            Title: "LLM Contract",
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", $"guidance:llm-contract:{repositoryId}"),
                    KeyValue("entity_type", "guidance"),
                    KeyValue("repository_id", repositoryId),
                    KeyValue("guidance_kind", "llm"),
                    KeyValue("head_branch", headBranch),
                ],
                sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderSolutionPage(
        string repositoryId,
        SolutionNode solution,
        IReadOnlyDictionary<EntityId, ProjectNode> projectById,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Solution: {solution.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{solution.Path}`");
        sb.AppendLine($"- Projects: {solution.ProjectIds.Count}");
        sb.AppendLine();
        sb.AppendLine("## Projects");

        foreach (var projectId in solution.ProjectIds)
        {
            if (projectById.TryGetValue(projectId, out var project))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(project.Id, project.Name)}");
            }
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(solution.Id),
            Title: solution.Name,
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", solution.Id.Value),
                    KeyValue("entity_type", "solution"),
                    KeyValue("repository_id", repositoryId),
                    KeyValue("solution_name", solution.Name),
                    KeyValue("solution_path", solution.Path),
                ],
                sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderProjectPage(
        string repositoryId,
        ProjectNode project,
        IReadOnlyDictionary<EntityId, PackageNode> packageById,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Project: {project.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{project.Path}`");
        sb.AppendLine($"- Discovery: `{project.DiscoveryMethod}`");

        if (project.TargetFrameworks.Count > 0)
        {
            sb.AppendLine($"- Target Frameworks: `{string.Join(", ", project.TargetFrameworks)}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Packages");

        foreach (var packageId in project.PackageIds)
        {
            if (!packageById.TryGetValue(packageId, out var package))
            {
                continue;
            }

            sb.AppendLine($"- {resolver.ToWikiLink(package.Id, package.Name)}");
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(project.Id),
            Title: project.Name,
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", project.Id.Value),
                    KeyValue("entity_type", "project"),
                    KeyValue("repository_id", repositoryId),
                    KeyValue("project_name", project.Name),
                    KeyValue("project_path", project.Path),
                    KeyValue("target_frameworks", ToInlineList(project.TargetFrameworks)),
                    KeyValue("discovery_method", project.DiscoveryMethod),
                ],
                sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderPackagePage(
        string repositoryId,
        PackageNode package,
        IReadOnlyDictionary<EntityId, IReadOnlyList<InheritedPackageTypeUsageNode>> inheritedPackageTypeUsageByPackageId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<string>> externalCallTargetsByPackageId,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Package: {package.Name}");
        sb.AppendLine();
        sb.AppendLine("## Declared Versions");

        foreach (var declared in package.DeclaredVersions.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{declared}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Resolved Versions");
        foreach (var resolved in package.ResolvedVersions.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{resolved}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Project Membership");
        sb.AppendLine("| project | project_path | declared_version | resolved_version |");
        sb.AppendLine("| --- | --- | --- | --- |");

        foreach (var membership in package.ProjectMemberships)
        {
            var declaredVersion = string.IsNullOrWhiteSpace(membership.DeclaredVersion) ? "-" : membership.DeclaredVersion;
            var resolvedVersion = string.IsNullOrWhiteSpace(membership.ResolvedVersion) ? "-" : membership.ResolvedVersion;
            sb.AppendLine(
                $"| {resolver.ToMarkdownLink(membership.ProjectId, membership.ProjectName)} | `{membership.ProjectPath}` | `{declaredVersion}` | `{resolvedVersion}` |");
        }

        var emittedAnchorIds = new HashSet<string>(StringComparer.Ordinal);

        if (package.DeclarationDependencyTargetFirst.UsageCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Declaration Dependencies (External Type -> Internal Type)");

            foreach (var externalType in package.DeclarationDependencyTargetFirst.ExternalTypes)
            {
                var anchor = WikiPathResolver.BuildPackageExternalTypeAnchor(package.CanonicalKey, externalType.ExternalTypeDisplayName);
                if (emittedAnchorIds.Add(anchor))
                {
                    sb.AppendLine($"<a id=\"{anchor}\"></a>");
                }
                sb.AppendLine($"- `{externalType.ExternalTypeDisplayName}` ({externalType.UsageCount})");

                foreach (var internalType in externalType.InternalTypes)
                {
                    var internalTypeDisplay = resolver.ToWikiLink(internalType.InternalTypeId, internalType.InternalTypeName);
                    sb.AppendLine($"  - {internalTypeDisplay} ({internalType.UsageCount})");
                }
            }
        }

        if (inheritedPackageTypeUsageByPackageId.TryGetValue(package.Id, out var inheritedPackageTypes) && inheritedPackageTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Inherited Package Types");

            foreach (var inheritedPackageType in inheritedPackageTypes)
            {
                var anchor = WikiPathResolver.BuildPackageExternalTypeAnchor(package.CanonicalKey, inheritedPackageType.ExternalTypeDisplayName);
                if (emittedAnchorIds.Add(anchor))
                {
                    sb.AppendLine($"<a id=\"{anchor}\"></a>");
                }

                sb.AppendLine($"- `{inheritedPackageType.ExternalTypeDisplayName}` ({inheritedPackageType.UsageCount})");

                foreach (var internalType in inheritedPackageType.InternalTypes)
                {
                    sb.AppendLine($"  - {resolver.ToWikiLink(internalType.InternalTypeId, internalType.InternalTypeName)} ({internalType.UsageCount})");
                }
            }
        }

        if (package.MethodBodyDependencyTargetFirst.UsageCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Method Body Dependencies (External Type -> Internal Method)");

            foreach (var externalType in package.MethodBodyDependencyTargetFirst.ExternalTypes)
            {
                var anchor = WikiPathResolver.BuildPackageExternalTypeAnchor(package.CanonicalKey, externalType.ExternalTypeDisplayName);
                if (emittedAnchorIds.Add(anchor))
                {
                    sb.AppendLine($"<a id=\"{anchor}\"></a>");
                }

                sb.AppendLine($"- `{externalType.ExternalTypeDisplayName}` ({externalType.UsageCount})");

                foreach (var internalMethod in externalType.InternalMethods)
                {
                    var methodDisplay = resolver.ToWikiLink(internalMethod.InternalMethodId, internalMethod.InternalMethodDisplayName);
                    sb.AppendLine($"  - {methodDisplay} ({internalMethod.UsageCount})");
                }
            }
        }

        if (externalCallTargetsByPackageId.TryGetValue(package.Id, out var externalCallTargets) && externalCallTargets.Count > 0)
        {
            var additionalTargets = externalCallTargets
                .Select(target => new
                {
                    Target = target,
                    Anchor = WikiPathResolver.BuildPackageExternalTypeAnchor(package.CanonicalKey, target),
                })
                .Where(x => !emittedAnchorIds.Contains(x.Anchor))
                .OrderBy(x => x.Target, StringComparer.Ordinal)
                .ToArray();

            if (additionalTargets.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## External Type Anchors");
                foreach (var additionalTarget in additionalTargets)
                {
                    sb.AppendLine($"<a id=\"{additionalTarget.Anchor}\"></a>");
                    sb.AppendLine($"- `{additionalTarget.Target}`");
                }
            }
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(package.Id),
            Title: package.Name,
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", package.Id.Value),
                    KeyValue("entity_type", "package"),
                    KeyValue("repository_id", repositoryId),
                    KeyValue("package_id", package.Name),
                    KeyValue("package_key", package.CanonicalKey),
                ],
                sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderUnknownPackageAttributionPage(
        string repositoryId,
        DependencyAttributionCatalog dependencyAttribution,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        WikiPathResolver resolver)
    {
        const string pagePath = "packages/unknown-package-attribution.md";

        var sb = new StringBuilder();
        sb.AppendLine("# Unknown Package Attribution");

        AppendUnknownDependencyUsageSection(
            sb,
            "Declaration Dependency Usage (Unknown Package Attribution)",
            dependencyAttribution.DeclarationUnknown,
            methodById,
            resolver);
        AppendUnknownDependencyUsageSection(
            sb,
            "Method Body Dependency Usage (Unknown Package Attribution)",
            dependencyAttribution.MethodBodyUnknown,
            methodById,
            resolver);

        return new WikiPage(
            RelativePath: pagePath,
            Title: "Unknown Package Attribution",
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", $"dependency-attribution:unknown:{repositoryId}"),
                    KeyValue("entity_type", "dependency-attribution-unknown"),
                    KeyValue("repository_id", repositoryId),
                ],
                sb.ToString().TrimEnd()));
    }

    private static void AppendUnknownDependencyUsageSection(
        StringBuilder sb,
        string heading,
        UnknownDependencyUsageCatalog catalog,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        WikiPathResolver resolver)
    {
        sb.AppendLine();
        sb.AppendLine($"## {heading}");

        if (catalog.UsageCount == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        var unknownMethodRows = new List<(EntityId MethodId, string MethodAlias, string TargetTypeName, string AttributionReason, string? TargetResolutionReason, int UsageCount)>();

        foreach (var namespaceUsage in catalog.Namespaces)
        {
            var namespaceDisplay = namespaceUsage.NamespaceId is { } namespaceId
                ? resolver.ToWikiLink(namespaceId, namespaceUsage.NamespaceName)
                : namespaceUsage.NamespaceName;
            sb.AppendLine($"- {namespaceDisplay} ({namespaceUsage.UsageCount})");

            foreach (var typeUsage in namespaceUsage.Types)
            {
                var typeDisplay = resolver.ToWikiLink(typeUsage.TypeId, typeUsage.TypeName);
                sb.AppendLine($"  - {typeDisplay} ({typeUsage.UsageCount})");

                foreach (var methodUsage in typeUsage.Methods)
                {
                    var methodAlias = methodById.TryGetValue(methodUsage.MethodId, out var method)
                        ? FormatMethodLinkAlias(method)
                        : methodUsage.MethodSignature;
                    var methodDisplay = resolver.ToWikiLink(methodUsage.MethodId, methodAlias);
                    var reasonSuffix = string.IsNullOrWhiteSpace(methodUsage.TargetResolutionReason)
                        ? string.Empty
                        : $", resolution: {methodUsage.TargetResolutionReason}";
                    sb.AppendLine(
                        $"    - {methodDisplay} -> `{methodUsage.TargetTypeName}` (attribution: {methodUsage.AttributionReason}{reasonSuffix}) ({methodUsage.UsageCount})");

                    unknownMethodRows.Add((
                        MethodId: methodUsage.MethodId,
                        MethodAlias: methodAlias,
                        TargetTypeName: methodUsage.TargetTypeName,
                        AttributionReason: methodUsage.AttributionReason,
                        TargetResolutionReason: methodUsage.TargetResolutionReason,
                        UsageCount: methodUsage.UsageCount));
                }
            }
        }

        var unresolvedReasonBuckets = unknownMethodRows
            .Where(x => !string.IsNullOrWhiteSpace(x.TargetResolutionReason))
            .GroupBy(x => x.TargetResolutionReason!, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();

        if (unresolvedReasonBuckets.Length == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("### Unresolved External Targets");

        foreach (var unresolvedReasonBucket in unresolvedReasonBuckets)
        {
            var reasonUsageCount = unresolvedReasonBucket.Sum(x => x.UsageCount);
            sb.AppendLine($"- {unresolvedReasonBucket.Key} ({reasonUsageCount})");

            foreach (var unresolvedUsage in unresolvedReasonBucket
                         .OrderBy(x => x.TargetTypeName, StringComparer.Ordinal)
                         .ThenBy(x => x.MethodAlias, StringComparer.Ordinal)
                         .ThenBy(x => x.MethodId.Value, StringComparer.Ordinal))
            {
                var unresolvedMethodDisplay = resolver.ToWikiLink(unresolvedUsage.MethodId, unresolvedUsage.MethodAlias);
                sb.AppendLine($"  - {unresolvedMethodDisplay} -> `{unresolvedUsage.TargetTypeName}` ({unresolvedUsage.UsageCount})");
            }
        }
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<NamespaceDeclarationNode>> BuildNamespaceBacklinksByFileId(
        IReadOnlyList<NamespaceDeclarationNode> namespaces,
        IReadOnlyDictionary<EntityId, FileNode> fileById)
    {
        return namespaces
            .SelectMany(namespaceDeclaration => namespaceDeclaration.DeclarationFileIds.Select(fileId => (fileId, namespaceDeclaration)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var filePath = fileById.TryGetValue(x.Key, out var file) ? file.Path : string.Empty;

                    return (IReadOnlyList<NamespaceDeclarationNode>)x
                    .Select(v => v.namespaceDeclaration)
                    .DistinctBy(v => v.Id)
                    .OrderBy(v => filePath, StringComparer.Ordinal)
                    .ThenBy(v => ResolveDeclarationLocationSortLine(v.DeclarationLocations, filePath))
                    .ThenBy(v => ResolveDeclarationLocationSortColumn(v.DeclarationLocations, filePath))
                    .ThenBy(v => v.Path, StringComparer.Ordinal)
                    .ThenBy(v => v.Name, StringComparer.Ordinal)
                    .ThenBy(v => v.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                });
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<TypeDeclarationNode>> BuildTypeBacklinksByFileId(
        IReadOnlyList<TypeDeclarationNode> types,
        IReadOnlyDictionary<EntityId, FileNode> fileById)
    {
        return types
            .SelectMany(typeDeclaration => typeDeclaration.DeclarationFileIds.Select(fileId => (fileId, typeDeclaration)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var filePath = fileById.TryGetValue(x.Key, out var file) ? file.Path : string.Empty;

                    return (IReadOnlyList<TypeDeclarationNode>)x
                    .Select(v => v.typeDeclaration)
                    .DistinctBy(v => v.Id)
                    .OrderBy(v => filePath, StringComparer.Ordinal)
                    .ThenBy(v => ResolveDeclarationLocationSortLine(v.DeclarationLocations, filePath))
                    .ThenBy(v => ResolveDeclarationLocationSortColumn(v.DeclarationLocations, filePath))
                    .ThenBy(v => v.Path, StringComparer.Ordinal)
                    .ThenBy(v => v.Name, StringComparer.Ordinal)
                    .ThenBy(v => v.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                });
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<MemberDeclarationNode>> BuildMemberBacklinksByFileId(
        IReadOnlyList<MemberDeclarationNode> members,
        IReadOnlyDictionary<EntityId, FileNode> fileById)
    {
        return members
            .SelectMany(member => member.DeclarationFileIds.Select(fileId => (fileId, member)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var filePath = fileById.TryGetValue(x.Key, out var file) ? file.Path : string.Empty;

                    return (IReadOnlyList<MemberDeclarationNode>)x
                    .Select(v => v.member)
                    .DistinctBy(v => v.Id)
                    .OrderBy(v => filePath, StringComparer.Ordinal)
                    .ThenBy(v => ResolveDeclarationLocationSortLine(v.DeclarationLocations, filePath))
                    .ThenBy(v => ResolveDeclarationLocationSortColumn(v.DeclarationLocations, filePath))
                    .ThenBy(v => v.Kind.ToString(), StringComparer.Ordinal)
                    .ThenBy(v => v.Name, StringComparer.Ordinal)
                    .ThenBy(v => v.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                });
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<MethodDeclarationNode>> BuildMethodBacklinksByFileId(
        IReadOnlyList<MethodDeclarationNode> methods,
        IReadOnlyDictionary<EntityId, FileNode> fileById)
    {
        return methods
            .SelectMany(method => method.DeclarationFileIds.Select(fileId => (fileId, method)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var filePath = fileById.TryGetValue(x.Key, out var file) ? file.Path : string.Empty;

                    return (IReadOnlyList<MethodDeclarationNode>)x
                    .Select(v => v.method)
                    .DistinctBy(v => v.Id)
                    .OrderBy(v => filePath, StringComparer.Ordinal)
                    .ThenBy(v => ResolveDeclarationLocationSortLine(v.DeclarationLocations, filePath))
                    .ThenBy(v => ResolveDeclarationLocationSortColumn(v.DeclarationLocations, filePath))
                    .ThenBy(v => v.Signature, StringComparer.Ordinal)
                    .ThenBy(v => v.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                });
    }

    private static int ResolveDeclarationLocationSortLine(
        IReadOnlyList<DeclarationLocationNode> locations,
        string filePath)
    {
        var location = locations
            .Where(x => x.FilePath.Equals(filePath, StringComparison.Ordinal))
            .OrderBy(x => x.Line)
            .ThenBy(x => x.Column)
            .FirstOrDefault();

        return location?.Line ?? int.MaxValue;
    }

    private static int ResolveDeclarationLocationSortColumn(
        IReadOnlyList<DeclarationLocationNode> locations,
        string filePath)
    {
        var location = locations
            .Where(x => x.FilePath.Equals(filePath, StringComparison.Ordinal))
            .OrderBy(x => x.Line)
            .ThenBy(x => x.Column)
            .FirstOrDefault();

        return location?.Column ?? int.MaxValue;
    }

    private static WikiPage RenderNamespacePage(
        string repositoryId,
        NamespaceDeclarationNode namespaceDeclaration,
        IReadOnlyDictionary<EntityId, NamespaceDeclarationNode> namespaceById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, NamespaceStructuralMetricRollupNode> namespaceMetricRollupByNamespaceId,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Namespace: {namespaceDeclaration.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{namespaceDeclaration.Path}`");

        if (namespaceDeclaration.ParentNamespaceId is { } parentNamespaceId && namespaceById.TryGetValue(parentNamespaceId, out var parentNamespace))
        {
            sb.AppendLine($"- Parent: {resolver.ToWikiLink(parentNamespace.Id, parentNamespace.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Child Namespaces");

        foreach (var childNamespaceId in namespaceDeclaration.ChildNamespaceIds)
        {
            if (!namespaceById.TryGetValue(childNamespaceId, out var childNamespace))
            {
                continue;
            }

            sb.AppendLine($"- {resolver.ToWikiLink(childNamespace.Id, childNamespace.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Contained Types");

        foreach (var containedTypeId in namespaceDeclaration.ContainedTypeIds)
        {
            if (!typeById.TryGetValue(containedTypeId, out var typeDeclaration))
            {
                continue;
            }

            sb.AppendLine($"- {resolver.ToWikiLink(typeDeclaration.Id, typeDeclaration.Name)} ({typeDeclaration.Kind.ToString().ToLowerInvariant()})");
        }

        if (namespaceMetricRollupByNamespaceId.TryGetValue(namespaceDeclaration.Id, out var namespaceMetrics))
        {
            sb.AppendLine();
            sb.AppendLine("## Metrics");
            sb.AppendLine("### Direct");
            AppendStructuralMetricScopeSummary(sb, namespaceMetrics.Direct);
            sb.AppendLine("### Recursive");
            AppendStructuralMetricScopeSummary(sb, namespaceMetrics.Recursive);
        }

        var frontMatter = new List<KeyValuePair<string, string>>
        {
            KeyValue("entity_id", namespaceDeclaration.Id.Value),
            KeyValue("entity_type", "namespace"),
            KeyValue("repository_id", repositoryId),
            KeyValue("namespace_name", namespaceDeclaration.Name),
            KeyValue("namespace_path", namespaceDeclaration.Path),
        };

        if (namespaceDeclaration.ParentNamespaceId is { } parentId && namespaceById.TryGetValue(parentId, out var parent))
        {
            frontMatter.Add(KeyValue("parent_namespace_id", parentId.Value));
            frontMatter.Add(KeyValue("parent_namespace_name", parent.Name));
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(namespaceDeclaration.Id),
            Title: namespaceDeclaration.Name,
            Markdown: WithFrontMatter(frontMatter, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderTypePage(
        string repositoryId,
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, NamespaceDeclarationNode> namespaceById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, MemberDeclarationNode> memberById,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> readMethodIdsByTargetMemberId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> writeMethodIdsByTargetMemberId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> inheritedByTypeIdsByBaseTypeId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> implementedByTypeIdsByInterfaceTypeId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> endpointIdsByDeclaringTypeId,
        IReadOnlyDictionary<EntityId, EndpointNode> endpointById,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        IReadOnlyList<ProjectNode> projects,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Type: {typeDeclaration.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: {typeDeclaration.Kind.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- Accessibility: {typeDeclaration.Accessibility.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- Canonical Path: `{typeDeclaration.Path}`");
        sb.AppendLine($"- Arity: {typeDeclaration.Arity}");
        sb.AppendLine($"- Is Partial: {typeDeclaration.IsPartialType.ToString().ToLowerInvariant()}");

        if (typeDeclaration.NamespaceId is { } namespaceId && namespaceById.TryGetValue(namespaceId, out var namespaceDeclaration))
        {
            sb.AppendLine($"- Namespace: {resolver.ToWikiLink(namespaceDeclaration.Id, namespaceDeclaration.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Nesting Context");
        if (typeDeclaration.DeclaringTypeId is { } declaringTypeId && typeById.TryGetValue(declaringTypeId, out var declaringType))
        {
            sb.AppendLine($"- Declaring Type: {resolver.ToWikiLink(declaringType.Id, declaringType.Name)}");
        }
        else
        {
            sb.AppendLine("- Declaring Type: none");
        }

        sb.AppendLine();
        sb.AppendLine("## Inherits From");
        AppendTypeReferenceSection(sb, typeDeclaration.DirectBaseTypes, typeById, resolver);

        sb.AppendLine();
        sb.AppendLine("## Inherited By");
        AppendReverseTypeRelationshipSection(sb, typeDeclaration.Id, inheritedByTypeIdsByBaseTypeId, typeById, resolver);

        sb.AppendLine();
        sb.AppendLine("## Implements");
        AppendTypeReferenceSection(sb, typeDeclaration.DirectInterfaceTypes, typeById, resolver);

        sb.AppendLine();
        sb.AppendLine("## Implemented By");
        AppendReverseTypeRelationshipSection(sb, typeDeclaration.Id, implementedByTypeIdsByInterfaceTypeId, typeById, resolver);

        sb.AppendLine();
        sb.AppendLine("## Members");
        AppendMemberSection(sb, "Properties", typeDeclaration, memberById, MemberDeclarationKind.Property);
        AppendMemberSection(sb, "Fields", typeDeclaration, memberById, MemberDeclarationKind.Field);
        AppendMemberSection(sb, "Record Parameters", typeDeclaration, memberById, MemberDeclarationKind.RecordParameter);
        AppendMemberSection(sb, "Enum Members", typeDeclaration, memberById, MemberDeclarationKind.EnumMember);

        sb.AppendLine();
        sb.AppendLine("## Methods");
        AppendMethodSection(sb, typeDeclaration, methodById, resolver);

        sb.AppendLine();
        sb.AppendLine("## Member Data Flow");
        AppendMemberDataFlowSection(
            sb,
            "Properties",
            typeDeclaration,
            memberById,
            methodById,
            readMethodIdsByTargetMemberId,
            writeMethodIdsByTargetMemberId,
            MemberDeclarationKind.Property,
            resolver);
        AppendMemberDataFlowSection(
            sb,
            "Fields",
            typeDeclaration,
            memberById,
            methodById,
            readMethodIdsByTargetMemberId,
            writeMethodIdsByTargetMemberId,
            MemberDeclarationKind.Field,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Endpoints");
        if (!endpointIdsByDeclaringTypeId.TryGetValue(typeDeclaration.Id, out var endpointIds) || endpointIds.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var endpointId in endpointIds)
            {
                if (!endpointById.TryGetValue(endpointId, out var endpoint))
                {
                    continue;
                }

                sb.AppendLine($"- {resolver.ToWikiLink(endpoint.Id, endpoint.Name)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Metrics");
        AppendTypeMetricsSection(sb, typeDeclaration.Metrics);

        sb.AppendLine();
        sb.AppendLine("## Dependency Rollup");
        sb.AppendLine("### Declaration Packages");
        if (typeDeclaration.DependencyRollup.DeclarationPackages.Count == 0
            && typeDeclaration.DependencyRollup.DeclarationUnknownUsageCount == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var usage in typeDeclaration.DependencyRollup.DeclarationPackages)
            {
                sb.AppendLine($"- {resolver.ToWikiLink(usage.PackageId, usage.PackageName)} ({usage.UsageCount})");
            }

            if (typeDeclaration.DependencyRollup.DeclarationUnknownUsageCount > 0)
            {
                sb.AppendLine($"- Unknown package attribution ({typeDeclaration.DependencyRollup.DeclarationUnknownUsageCount})");
            }
        }

        sb.AppendLine("### Method Body Packages");
        if (typeDeclaration.DependencyRollup.MethodBodyPackages.Count == 0
            && typeDeclaration.DependencyRollup.MethodBodyUnknownUsageCount == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var usage in typeDeclaration.DependencyRollup.MethodBodyPackages)
            {
                sb.AppendLine($"- {resolver.ToWikiLink(usage.PackageId, usage.PackageName)} ({usage.UsageCount})");
            }

            if (typeDeclaration.DependencyRollup.MethodBodyUnknownUsageCount > 0)
            {
                sb.AppendLine($"- Unknown package attribution ({typeDeclaration.DependencyRollup.MethodBodyUnknownUsageCount})");
            }
        }

        if (typeDeclaration.GenericParameters.Count > 0 || typeDeclaration.GenericConstraints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Generic Signature");

            if (typeDeclaration.GenericParameters.Count > 0)
            {
                sb.AppendLine($"- Parameters: `{string.Join(", ", typeDeclaration.GenericParameters)}`");
            }

            foreach (var constraint in typeDeclaration.GenericConstraints)
            {
                sb.AppendLine($"- Constraint: `{constraint}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Declaration Files");
        foreach (var declarationFileId in typeDeclaration.DeclarationFileIds)
        {
            if (fileById.TryGetValue(declarationFileId, out var file))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(file.Id, file.Path)}");
            }
        }

        var frontMatter = new List<KeyValuePair<string, string>>
        {
            KeyValue("entity_id", typeDeclaration.Id.Value),
            KeyValue("entity_type", "type"),
            KeyValue("repository_id", repositoryId),
            KeyValue("type_name", typeDeclaration.Name),
            KeyValue("type_kind", typeDeclaration.Kind.ToString().ToLowerInvariant()),
            KeyValue("type_path", typeDeclaration.Path),
            KeyValue("accessibility", typeDeclaration.Accessibility.ToString().ToLowerInvariant()),
            KeyValue("constructor_count", CountMethods(typeDeclaration, methodById, MethodDeclarationKind.Constructor).ToString()),
            KeyValue("method_count", CountMethods(typeDeclaration, methodById, MethodDeclarationKind.Method).ToString()),
            KeyValue("property_count", CountMembers(typeDeclaration, memberById, MemberDeclarationKind.Property).ToString()),
            KeyValue("field_count", CountMembers(typeDeclaration, memberById, MemberDeclarationKind.Field).ToString()),
            KeyValue("enum_member_count", CountMembers(typeDeclaration, memberById, MemberDeclarationKind.EnumMember).ToString()),
            KeyValue("record_parameter_count", CountMembers(typeDeclaration, memberById, MemberDeclarationKind.RecordParameter).ToString()),
            KeyValue("behavioral_method_count", CountMethods(typeDeclaration, methodById, MethodDeclarationKind.Method).ToString()),
        };

        if (typeDeclaration.IsNestedType)
        {
            frontMatter.Add(KeyValue("is_nested_type", "true"));
        }

        if (typeDeclaration.IsPartialType)
        {
            frontMatter.Add(KeyValue("is_partial_type", "true"));
        }

        if (typeDeclaration.NamespaceId is { } typeNamespaceId && namespaceById.TryGetValue(typeNamespaceId, out var typeNamespace))
        {
            frontMatter.Add(KeyValue("namespace_name", typeNamespace.Name));
        }

        if (typeDeclaration.DeclaringTypeId is { } parentTypeId && typeById.TryGetValue(parentTypeId, out var parentType))
        {
            frontMatter.Add(KeyValue("declaring_type_id", parentTypeId.Value));
            frontMatter.Add(KeyValue("declaring_type_name", parentType.Name));
        }

        if (TryResolvePrimaryProject(typeDeclaration, fileById, projects, out var primaryProject))
        {
            frontMatter.Add(KeyValue("primary_project_id", primaryProject.Id.Value));
            frontMatter.Add(KeyValue("primary_project_name", primaryProject.Name));
            frontMatter.Add(KeyValue("primary_assembly_name", primaryProject.Name));
            frontMatter.Add(KeyValue("primary_project_path", primaryProject.Path));
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(typeDeclaration.Id),
            Title: typeDeclaration.Name,
            Markdown: WithFrontMatter(frontMatter, sb.ToString().TrimEnd()));
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> BuildReverseTypeRelationshipMap(
        IReadOnlyList<TypeDeclarationNode> types,
        Func<TypeDeclarationNode, IEnumerable<EntityId>> forwardTargetSelector,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById)
    {
        return types
            .SelectMany(type => forwardTargetSelector(type).Select(targetTypeId => (targetTypeId, sourceTypeId: type.Id)))
            .GroupBy(x => x.targetTypeId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EntityId>)x
                    .Select(v => v.sourceTypeId)
                    .Distinct()
                    .OrderBy(v => typeById.TryGetValue(v, out var type) ? type.Path : string.Empty, StringComparer.Ordinal)
                    .ThenBy(v => typeById.TryGetValue(v, out var type) ? type.Name : string.Empty, StringComparer.Ordinal)
                    .ThenBy(v => v.Value, StringComparer.Ordinal)
                    .ToArray());
    }

    private static void AppendTypeReferenceSection(
        StringBuilder sb,
        IReadOnlyList<TypeReferenceNode> references,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        WikiPathResolver resolver)
    {
        if (references.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var reference in references)
        {
            if (reference.TypeId is { } referenceTypeId && typeById.TryGetValue(referenceTypeId, out var type))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(type.Id, type.Name)}");
                continue;
            }

            sb.AppendLine($"- {FormatTypeReference(reference)}");
        }
    }

    private static void AppendReverseTypeRelationshipSection(
        StringBuilder sb,
        EntityId targetTypeId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> sourceTypeIdsByTargetTypeId,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        WikiPathResolver resolver)
    {
        if (!sourceTypeIdsByTargetTypeId.TryGetValue(targetTypeId, out var sourceTypeIds) || sourceTypeIds.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var sourceTypeId in sourceTypeIds)
        {
            if (typeById.TryGetValue(sourceTypeId, out var sourceType))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(sourceType.Id, sourceType.Name)}");
            }
        }
    }

    private static void AppendMemberSection(
        StringBuilder sb,
        string sectionName,
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, MemberDeclarationNode> memberById,
        MemberDeclarationKind expectedKind)
    {
        var members = typeDeclaration.MemberIds
            .Where(memberById.ContainsKey)
            .Select(id => memberById[id])
            .Where(member => member.Kind == expectedKind)
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Id.Value, StringComparer.Ordinal)
            .ToArray();

        sb.AppendLine($"### {sectionName}");
        if (members.Length == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var member in members)
        {
            var declaredType = member.DeclaredType?.DisplayText;
            if (expectedKind == MemberDeclarationKind.EnumMember)
            {
                var constant = string.IsNullOrWhiteSpace(member.ConstantValue) ? "-" : member.ConstantValue;
                sb.AppendLine($"- {member.Name} = {constant}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(declaredType))
            {
                sb.AppendLine($"- {member.Name}");
                continue;
            }

            sb.AppendLine($"- {member.Name}: {FormatTypeReference(member.DeclaredType!)}");
        }
    }

    private static void AppendMethodSection(
        StringBuilder sb,
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        WikiPathResolver resolver)
    {
        var methods = typeDeclaration.MethodIds
            .Where(methodById.ContainsKey)
            .Select(id => methodById[id])
            .OrderBy(x => x.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(x => x.Signature, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .ToArray();

        if (methods.Length == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var method in methods)
        {
            sb.AppendLine($"- {resolver.ToWikiLink(method.Id, FormatMethodLinkAlias(method))}");
        }
    }

    private static void AppendTypeMetricsSection(StringBuilder sb, TypeMetricNode metrics)
    {
        sb.AppendLine("### Coupling");
        if (!metrics.HasCbo)
        {
            sb.AppendLine("- CBO: not available");
        }
        else
        {
            sb.AppendLine($"- CBO Declaration: {metrics.CboDeclaration}");
            sb.AppendLine($"- CBO Method Body: {metrics.CboMethodBody}");
            sb.AppendLine($"- CBO Total: {metrics.CboTotal}");
        }

        sb.AppendLine("### Method Complexity Rollup");
        sb.AppendLine($"- Methods With Metrics: {metrics.MethodMetricCount}");

        if (metrics.MethodMetricCount == 0)
        {
            sb.AppendLine("- Average Cyclomatic Complexity: n/a");
            sb.AppendLine("- Average Cognitive Complexity: n/a");
            sb.AppendLine("- Average Halstead Volume: n/a");
            sb.AppendLine("- Average Maintainability Index: n/a");
            return;
        }

        sb.AppendLine($"- Average Cyclomatic Complexity: {FormatMetricNumber(metrics.AverageCyclomaticComplexity)}");
        sb.AppendLine($"- Average Cognitive Complexity: {FormatMetricNumber(metrics.AverageCognitiveComplexity)}");
        sb.AppendLine($"- Average Halstead Volume: {FormatMetricNumber(metrics.AverageHalsteadVolume)}");
        sb.AppendLine($"- Average Maintainability Index: {FormatMetricNumber(metrics.AverageMaintainabilityIndex)}");
    }

    private static void AppendMethodMetricsSection(StringBuilder sb, MethodMetricNode metrics)
    {
        var coverageStatus = string.IsNullOrWhiteSpace(metrics.CoverageStatus)
            ? "unknown"
            : metrics.CoverageStatus;
        sb.AppendLine($"- Coverage Status: `{coverageStatus}`");
        sb.AppendLine($"- Cyclomatic Complexity: {FormatOptionalIntMetric(metrics.CyclomaticComplexity)}");
        sb.AppendLine($"- Cognitive Complexity: {FormatOptionalIntMetric(metrics.CognitiveComplexity)}");
        sb.AppendLine($"- Halstead Volume: {FormatOptionalDoubleMetric(metrics.HalsteadVolume)}");
        sb.AppendLine($"- Maintainability Index: {FormatOptionalDoubleMetric(metrics.MaintainabilityIndex)}");
    }

    private static void AppendStructuralMetricScopeSummary(StringBuilder sb, StructuralMetricScopeRollup rollup)
    {
        sb.AppendLine($"- Included In Ranking: {rollup.IncludedInRanking.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- Severity: `{rollup.Severity.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Methods With Metrics: {rollup.Metrics.MethodMetricCount}");
        sb.AppendLine($"- Types With CBO: {rollup.Metrics.TypeMetricCount}");

        if (rollup.Metrics.MethodMetricCount == 0)
        {
            sb.AppendLine("- Average Cyclomatic Complexity: n/a");
            sb.AppendLine("- Average Cognitive Complexity: n/a");
            sb.AppendLine("- Average Halstead Volume: n/a");
            sb.AppendLine("- Average Maintainability Index: n/a");
        }
        else
        {
            sb.AppendLine($"- Average Cyclomatic Complexity: {FormatMetricNumber(rollup.Metrics.AverageCyclomaticComplexity)}");
            sb.AppendLine($"- Average Cognitive Complexity: {FormatMetricNumber(rollup.Metrics.AverageCognitiveComplexity)}");
            sb.AppendLine($"- Average Halstead Volume: {FormatMetricNumber(rollup.Metrics.AverageHalsteadVolume)}");
            sb.AppendLine($"- Average Maintainability Index: {FormatMetricNumber(rollup.Metrics.AverageMaintainabilityIndex)}");
        }

        if (rollup.Metrics.TypeMetricCount == 0)
        {
            sb.AppendLine("- Average CBO Total: n/a");
        }
        else
        {
            sb.AppendLine($"- Average CBO Total: {FormatMetricNumber(rollup.Metrics.AverageCboTotal)}");
        }
    }

    private static string FormatOptionalIntMetric(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
    }

    private static string FormatOptionalDoubleMetric(double? value)
    {
        return value.HasValue ? FormatMetricNumber(value.Value) : "n/a";
    }

    private static string FormatMetricNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void AppendMemberDataFlowSection(
        StringBuilder sb,
        string sectionName,
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, MemberDeclarationNode> memberById,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> readMethodIdsByTargetMemberId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> writeMethodIdsByTargetMemberId,
        MemberDeclarationKind expectedKind,
        WikiPathResolver resolver)
    {
        var members = typeDeclaration.MemberIds
            .Where(memberById.ContainsKey)
            .Select(id => memberById[id])
            .Where(member => member.Kind == expectedKind)
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Id.Value, StringComparer.Ordinal)
            .ToArray();

        sb.AppendLine($"### {sectionName}");
        if (members.Length == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        sb.AppendLine("| member | read_count | write_count | readers | writers |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- |");

        foreach (var member in members)
        {
            var readers = readMethodIdsByTargetMemberId.TryGetValue(member.Id, out var readMethodIds)
                ? readMethodIds
                    .Where(methodById.ContainsKey)
                    .Select(id => methodById[id])
                    .OrderBy(x => x.Kind.ToString(), StringComparer.Ordinal)
                    .ThenBy(x => x.Signature, StringComparer.Ordinal)
                    .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
                    .Select(x => resolver.ToMarkdownLink(x.Id, FormatMethodLinkAlias(x)))
                    .ToArray()
                : [];
            var writers = writeMethodIdsByTargetMemberId.TryGetValue(member.Id, out var writeMethodIds)
                ? writeMethodIds
                    .Where(methodById.ContainsKey)
                    .Select(id => methodById[id])
                    .OrderBy(x => x.Kind.ToString(), StringComparer.Ordinal)
                    .ThenBy(x => x.Signature, StringComparer.Ordinal)
                    .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
                    .Select(x => resolver.ToMarkdownLink(x.Id, FormatMethodLinkAlias(x)))
                    .ToArray()
                : [];

            var readersCell = readers.Length == 0 ? "none" : string.Join(", ", readers);
            var writersCell = writers.Length == 0 ? "none" : string.Join(", ", writers);
            sb.AppendLine($"| {EscapePipes(member.Name)} | {readers.Length} | {writers.Length} | {readersCell} | {writersCell} |");
        }
    }

    private static int CountMethods(
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        MethodDeclarationKind methodKind)
    {
        return typeDeclaration.MethodIds
            .Where(methodById.ContainsKey)
            .Select(id => methodById[id])
            .Count(x => x.Kind == methodKind);
    }

    private static int CountMembers(
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, MemberDeclarationNode> memberById,
        MemberDeclarationKind memberKind)
    {
        return typeDeclaration.MemberIds
            .Where(memberById.ContainsKey)
            .Select(id => memberById[id])
            .Count(x => x.Kind == memberKind);
    }

    private static void AppendMethodRelationshipSection(
        StringBuilder sb,
        EntityId sourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> targetMethodIdsBySourceMethodId,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        WikiPathResolver resolver)
    {
        if (!targetMethodIdsBySourceMethodId.TryGetValue(sourceMethodId, out var targetMethodIds) || targetMethodIds.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var targetMethodId in targetMethodIds)
        {
            if (methodById.TryGetValue(targetMethodId, out var targetMethod))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(targetMethod.Id, FormatMethodLinkAlias(targetMethod))}");
            }
            else
            {
                sb.AppendLine("- unresolved target");
            }
        }
    }

    private static void AppendMethodMemberAccessSection(
        StringBuilder sb,
        EntityId sourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> targetMemberIdsBySourceMethodId,
        IReadOnlyDictionary<EntityId, MemberDeclarationNode> memberById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        WikiPathResolver resolver)
    {
        if (!targetMemberIdsBySourceMethodId.TryGetValue(sourceMethodId, out var targetMemberIds) || targetMemberIds.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        var members = targetMemberIds
            .Where(memberById.ContainsKey)
            .Select(id => memberById[id])
            .OrderBy(x => x.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .ToArray();

        if (members.Length == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var member in members)
        {
            var kind = member.Kind.ToString().ToLowerInvariant();
            if (typeById.TryGetValue(member.DeclaringTypeId, out var declaringType))
            {
                sb.AppendLine($"- {kind}: {member.Name} ({resolver.ToWikiLink(declaringType.Id, declaringType.Name)})");
                continue;
            }

            sb.AppendLine($"- {kind}: {member.Name}");
        }
    }

    private static void AppendOutgoingCallSection(
        StringBuilder sb,
        EntityId sourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<MethodRelationNode>> callRelationsBySourceMethodId,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyList<PackageNode> packages,
        WikiPathResolver resolver)
    {
        if (!callRelationsBySourceMethodId.TryGetValue(sourceMethodId, out var callRelations) || callRelations.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (var relation in callRelations)
        {
            if (relation.TargetMethodId is { } targetMethodId && methodById.TryGetValue(targetMethodId, out var targetMethod))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(targetMethod.Id, FormatMethodLinkAlias(targetMethod))}");
                continue;
            }

            if (relation.ExternalTargetType is not null)
            {
                var assemblySuffix = string.IsNullOrWhiteSpace(relation.ExternalAssemblyName)
                    ? string.Empty
                    : $" ({relation.ExternalAssemblyName})";
                var unresolvedReasonSuffix = string.IsNullOrWhiteSpace(relation.ResolutionReason)
                    ? string.Empty
                    : $": {relation.ResolutionReason}";
                var statusSuffix = relation.ExternalTargetType.ResolutionStatus switch
                {
                    DeclarationResolutionStatus.ExternalStub => " (external)",
                    DeclarationResolutionStatus.SourceTextFallback => $" (unresolved{unresolvedReasonSuffix})",
                    DeclarationResolutionStatus.Unresolved => $" (unresolved{unresolvedReasonSuffix})",
                    _ => string.Empty,
                };

                if (TryResolveExternalTargetPackage(relation, packages, out var package))
                {
                    var deepLink = resolver.ToPackageExternalTypeMarkdownLink(
                        package.Id,
                        package.CanonicalKey,
                        relation.ExternalTargetType.DisplayText,
                        relation.ExternalTargetType.DisplayText);
                    sb.AppendLine($"- {deepLink}{assemblySuffix}{statusSuffix}");
                }
                else
                {
                    sb.AppendLine($"- {relation.ExternalTargetType.DisplayText}{assemblySuffix}{statusSuffix}");
                }
                continue;
            }

            sb.AppendLine("- unresolved target");
        }
    }

    private static bool TryResolveExternalTargetPackage(
        MethodRelationNode relation,
        IReadOnlyList<PackageNode> packages,
        out PackageNode package)
    {
        var assemblyName = relation.ExternalAssemblyName;
        var externalTypeName = relation.ExternalTargetType?.DisplayText ?? string.Empty;

        var matches = packages
            .Select(candidate => new
            {
                Package = candidate,
                Score = GetExternalTargetPackageMatchScore(candidate, assemblyName, externalTypeName),
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Package.Name.Length)
            .ThenBy(candidate => candidate.Package.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Package.Id.Value, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            package = null!;
            return false;
        }

        var topScore = matches[0].Score;
        var topMatches = matches
            .Where(candidate => candidate.Score == topScore)
            .ToArray();

        if (topMatches.Length == 1)
        {
            package = topMatches[0].Package;
            return true;
        }

        package = null!;
        return false;
    }

    private static bool TryResolvePackageForExternalTypeName(
        string externalTypeName,
        IReadOnlyList<PackageNode> packages,
        out PackageNode package)
    {
        var matches = packages
            .Select(candidate => new
            {
                Package = candidate,
                Score = GetExternalTargetPackageMatchScore(candidate, null, externalTypeName),
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Package.Name.Length)
            .ThenBy(candidate => candidate.Package.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Package.Id.Value, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            package = null!;
            return false;
        }

        var topScore = matches[0].Score;
        var topMatches = matches
            .Where(candidate => candidate.Score == topScore)
            .ToArray();

        if (topMatches.Length == 1)
        {
            package = topMatches[0].Package;
            return true;
        }

        package = null!;
        return false;
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyList<InheritedPackageTypeUsageNode>> BuildInheritedPackageTypeUsageByPackageId(
        IReadOnlyList<TypeDeclarationNode> types,
        IReadOnlyList<PackageNode> packages)
    {
        var rows = types
            .SelectMany(type => type.DirectBaseTypes
                .Where(baseType =>
                    baseType.ResolutionStatus == DeclarationResolutionStatus.ExternalStub
                    && !string.IsNullOrWhiteSpace(baseType.DisplayText))
                .Select(baseType => TryResolvePackageForExternalTypeName(baseType.DisplayText, packages, out var package)
                    ? new InheritedPackageTypeUsageRow(
                        PackageId: package.Id,
                        ExternalTypeDisplayName: baseType.DisplayText,
                        InternalTypeId: type.Id,
                        InternalTypeName: type.Name,
                        UsageCount: 1)
                    : null))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        return rows
            .GroupBy(x => x.PackageId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<InheritedPackageTypeUsageNode>)x
                    .GroupBy(v => v.ExternalTypeDisplayName)
                    .Select(group => new InheritedPackageTypeUsageNode(
                        ExternalTypeDisplayName: group.Key,
                        UsageCount: group.Sum(v => v.UsageCount),
                        InternalTypes: group
                            .GroupBy(v => (v.InternalTypeId, v.InternalTypeName))
                            .Select(internalType => new InheritedPackageInternalTypeUsageNode(
                                InternalTypeId: internalType.Key.InternalTypeId,
                                InternalTypeName: internalType.Key.InternalTypeName,
                                UsageCount: internalType.Sum(v => v.UsageCount)))
                            .OrderBy(v => v.InternalTypeName, StringComparer.Ordinal)
                            .ThenBy(v => v.InternalTypeId.Value, StringComparer.Ordinal)
                            .ToArray()))
                    .OrderBy(v => v.ExternalTypeDisplayName, StringComparer.Ordinal)
                    .ToArray());
    }

    private static int GetExternalTargetPackageMatchScore(PackageNode package, string? assemblyName, string externalTypeName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var normalizedAssembly = assemblyName.Split(',', 2, StringSplitOptions.TrimEntries)[0]
                .Trim()
                .TrimEnd()
                .TrimEnd('.');
            if (normalizedAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                normalizedAssembly = normalizedAssembly[..^4];
            }

            if (normalizedAssembly.Equals(package.Name, StringComparison.OrdinalIgnoreCase))
            {
                return 3000 + package.Name.Length;
            }

            if (normalizedAssembly.StartsWith(package.Name + ".", StringComparison.OrdinalIgnoreCase))
            {
                return 2000 + package.Name.Length;
            }

            if (package.Name.StartsWith(normalizedAssembly + ".", StringComparison.OrdinalIgnoreCase))
            {
                return 1000 + package.Name.Length;
            }
        }

        if (!string.IsNullOrWhiteSpace(externalTypeName)
            && externalTypeName.StartsWith(package.Name + ".", StringComparison.OrdinalIgnoreCase))
        {
            return 500 + package.Name.Length;
        }

        return 0;
    }

    private static string FormatMethodLinkAlias(MethodDeclarationNode method)
    {
        var orderedParameters = method.Parameters
            .OrderBy(x => x.Ordinal)
            .ToArray();

        var genericSuffix = method.Arity > 0
            ? $"<{string.Join(", ", Enumerable.Range(1, method.Arity).Select(index => $"T{index}"))}>"
            : string.Empty;

        if (method.Kind == MethodDeclarationKind.Constructor)
        {
            if (orderedParameters.Length == 0)
            {
                return "ctor()";
            }

            return $"ctor({string.Join(", ", orderedParameters.Select(x => x.Type?.DisplayText ?? "unknown"))})";
        }

        if (orderedParameters.Length == 0)
        {
            return $"{method.Name}{genericSuffix}()";
        }

        return $"{method.Name}{genericSuffix}({string.Join(", ", orderedParameters.Select(x => x.Type?.DisplayText ?? "unknown"))})";
    }

    private static string FormatTypeReference(TypeReferenceNode reference)
    {
        return reference.ResolutionStatus switch
        {
            DeclarationResolutionStatus.ExternalStub => $"{reference.DisplayText} (external)",
            DeclarationResolutionStatus.SourceTextFallback => $"{reference.DisplayText} (source text fallback)",
            DeclarationResolutionStatus.Unresolved => $"{reference.DisplayText} (unresolved)",
            _ => reference.DisplayText,
        };
    }

    private static WikiPage RenderMethodPage(
        string repositoryId,
        MethodDeclarationNode methodDeclaration,
        IReadOnlyList<PackageNode> packages,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, MemberDeclarationNode> memberById,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> implementedMethodIdsBySourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> overriddenMethodIdsBySourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<MethodRelationNode>> callRelationsBySourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> readMemberIdsBySourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> writeMemberIdsBySourceMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> calledByMethodIdsByTargetMethodId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<EntityId>> endpointIdsByDeclaringMethodId,
        IReadOnlyDictionary<EntityId, EndpointNode> endpointById,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        WikiPathResolver resolver)
    {
        var declaringType = typeById.TryGetValue(methodDeclaration.DeclaringTypeId, out var resolvedDeclaringType)
            ? resolvedDeclaringType
            : null;

        var sb = new StringBuilder();
        sb.AppendLine($"# Method: {FormatMethodLinkAlias(methodDeclaration)}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: {methodDeclaration.Kind.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- Accessibility: {methodDeclaration.Accessibility.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- Arity: {methodDeclaration.Arity}");

        if (declaringType is not null)
        {
            sb.AppendLine($"- Declaring Type: {resolver.ToWikiLink(declaringType.Id, declaringType.Name)}");
        }
        else
        {
            sb.AppendLine($"- Declaring Type: `{methodDeclaration.DeclaringTypeId.Value}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Signature");
        sb.AppendLine($"- `{methodDeclaration.Signature}`");
        sb.AppendLine($"- Return Type: {(methodDeclaration.ReturnType is null ? "none" : FormatTypeReference(methodDeclaration.ReturnType))}");

        sb.AppendLine();
        sb.AppendLine("## Parameters");
        if (methodDeclaration.Parameters.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var parameter in methodDeclaration.Parameters.OrderBy(x => x.Ordinal))
            {
                var parameterType = parameter.Type is null
                    ? "unknown"
                    : FormatTypeReference(parameter.Type);
                sb.AppendLine($"- {parameter.Name}: {parameterType}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Endpoints");
        if (!endpointIdsByDeclaringMethodId.TryGetValue(methodDeclaration.Id, out var endpointIds) || endpointIds.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var endpointId in endpointIds)
            {
                if (!endpointById.TryGetValue(endpointId, out var endpoint))
                {
                    continue;
                }

                sb.AppendLine($"- {resolver.ToWikiLink(endpoint.Id, endpoint.Name)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Metrics");
        AppendMethodMetricsSection(sb, methodDeclaration.Metrics);

        sb.AppendLine();
        sb.AppendLine("## Implements");
        AppendMethodRelationshipSection(
            sb,
            methodDeclaration.Id,
            implementedMethodIdsBySourceMethodId,
            methodById,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Overrides");
        AppendMethodRelationshipSection(
            sb,
            methodDeclaration.Id,
            overriddenMethodIdsBySourceMethodId,
            methodById,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Calls");
        AppendOutgoingCallSection(
            sb,
            methodDeclaration.Id,
            callRelationsBySourceMethodId,
            methodById,
            packages,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Reads");
        AppendMethodMemberAccessSection(
            sb,
            methodDeclaration.Id,
            readMemberIdsBySourceMethodId,
            memberById,
            typeById,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Writes");
        AppendMethodMemberAccessSection(
            sb,
            methodDeclaration.Id,
            writeMemberIdsBySourceMethodId,
            memberById,
            typeById,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Called By");
        AppendMethodRelationshipSection(
            sb,
            methodDeclaration.Id,
            calledByMethodIdsByTargetMethodId,
            methodById,
            resolver);

        sb.AppendLine();
        sb.AppendLine("## Declaration Locations");
        if (methodDeclaration.DeclarationLocations.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var location in methodDeclaration.DeclarationLocations
                         .OrderBy(x => x.FilePath, StringComparer.Ordinal)
                         .ThenBy(x => x.Line)
                         .ThenBy(x => x.Column))
            {
                sb.AppendLine($"- `{location.FilePath}:{location.Line}:{location.Column}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Declaration Files");
        foreach (var declarationFileId in methodDeclaration.DeclarationFileIds)
        {
            if (fileById.TryGetValue(declarationFileId, out var file))
            {
                sb.AppendLine($"- {resolver.ToWikiLink(file.Id, file.Path)}");
            }
        }

        var frontMatter = new List<KeyValuePair<string, string>>
        {
            KeyValue("entity_id", methodDeclaration.Id.Value),
            KeyValue("entity_type", "method"),
            KeyValue("repository_id", repositoryId),
            KeyValue("method_name", methodDeclaration.Name),
            KeyValue("method_kind", methodDeclaration.Kind.ToString().ToLowerInvariant()),
            KeyValue("method_signature", methodDeclaration.Signature),
            KeyValue("accessibility", methodDeclaration.Accessibility.ToString().ToLowerInvariant()),
            KeyValue("declaring_type_id", methodDeclaration.DeclaringTypeId.Value),
        };

        if (declaringType is not null)
        {
            frontMatter.Add(KeyValue("declaring_type_name", declaringType.Name));
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(methodDeclaration.Id),
            Title: FormatMethodLinkAlias(methodDeclaration),
            Markdown: WithFrontMatter(frontMatter, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderEndpointGroupPage(
        string repositoryId,
        EndpointGroupNode endpointGroup,
        IReadOnlyDictionary<EntityId, NamespaceDeclarationNode> namespaceById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, EndpointNode> endpointById,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Endpoint Group: {endpointGroup.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Family: `{endpointGroup.Family}`");
        sb.AppendLine($"- Canonical Key: `{endpointGroup.CanonicalKey}`");
        sb.AppendLine($"- Authored Route Prefix: `{endpointGroup.AuthoredRoutePrefix}`");
        sb.AppendLine($"- Normalized Route Prefix: `{endpointGroup.NormalizedRoutePrefix}`");

        sb.AppendLine();
        sb.AppendLine("## Declaration Traceability");
        if (endpointGroup.NamespaceId is { } namespaceId && namespaceById.TryGetValue(namespaceId, out var namespaceDeclaration))
        {
            sb.AppendLine($"- Namespace: {resolver.ToWikiLink(namespaceDeclaration.Id, namespaceDeclaration.Name)}");
        }
        else
        {
            sb.AppendLine("- Namespace: none");
        }

        if (endpointGroup.DeclaringTypeId is { } declaringTypeId && typeById.TryGetValue(declaringTypeId, out var declaringType))
        {
            sb.AppendLine($"- Declaring Type: {resolver.ToWikiLink(declaringType.Id, declaringType.Name)}");
        }
        else
        {
            sb.AppendLine("- Declaring Type: none");
        }

        sb.AppendLine();
        sb.AppendLine("## Endpoints");
        var endpoints = endpointGroup.EndpointIds
            .Where(endpointById.ContainsKey)
            .Select(endpointId => endpointById[endpointId])
            .OrderBy(x => x.HttpMethod, StringComparer.Ordinal)
            .ThenBy(x => x.NormalizedRouteKey, StringComparer.Ordinal)
            .ThenBy(x => x.CanonicalSignature, StringComparer.Ordinal)
            .ToArray();
        if (endpoints.Length == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var endpoint in endpoints)
            {
                sb.AppendLine($"- {resolver.ToWikiLink(endpoint.Id, endpoint.Name)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Declaration Files");
        if (endpointGroup.DeclarationFileIds.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var declarationFileId in endpointGroup.DeclarationFileIds)
            {
                if (fileById.TryGetValue(declarationFileId, out var file))
                {
                    sb.AppendLine($"- {resolver.ToWikiLink(file.Id, file.Path)}");
                }
            }
        }

        var frontMatter = new List<KeyValuePair<string, string>>
        {
            KeyValue("entity_id", endpointGroup.Id.Value),
            KeyValue("entity_type", "endpoint_group"),
            KeyValue("repository_id", repositoryId),
            KeyValue("endpoint_family", endpointGroup.Family),
            KeyValue("endpoint_group_key", endpointGroup.CanonicalKey),
        };

        return new WikiPage(
            RelativePath: resolver.GetPath(endpointGroup.Id),
            Title: endpointGroup.Name,
            Markdown: WithFrontMatter(frontMatter, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderEndpointPage(
        string repositoryId,
        EndpointNode endpoint,
        IReadOnlyDictionary<EntityId, NamespaceDeclarationNode> namespaceById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, EndpointGroupNode> endpointGroupById,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        WikiPathResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Endpoint: {endpoint.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Family: `{endpoint.Family}`");
        sb.AppendLine($"- Kind: `{endpoint.Kind}`");
        sb.AppendLine($"- HTTP Method: `{endpoint.HttpMethod}`");
        sb.AppendLine($"- Authored Route: `{endpoint.AuthoredRouteTemplate}`");
        sb.AppendLine($"- Normalized Route Key: `{endpoint.NormalizedRouteKey}`");
        sb.AppendLine($"- Confidence: `{endpoint.Confidence.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Rule: `{endpoint.RuleId}`");
        sb.AppendLine($"- Rule Version: `{endpoint.RuleVersion}`");
        sb.AppendLine($"- Rule Source: `{endpoint.RuleSource}`");
        sb.AppendLine($"- Signature: `{endpoint.CanonicalSignature}`");

        sb.AppendLine();
        sb.AppendLine("## Declaration Traceability");
        if (endpoint.NamespaceId is { } namespaceId && namespaceById.TryGetValue(namespaceId, out var namespaceDeclaration))
        {
            sb.AppendLine($"- Namespace: {resolver.ToWikiLink(namespaceDeclaration.Id, namespaceDeclaration.Name)}");
        }
        else
        {
            sb.AppendLine("- Namespace: none");
        }

        if (endpoint.DeclaringTypeId is { } declaringTypeId && typeById.TryGetValue(declaringTypeId, out var declaringType))
        {
            sb.AppendLine($"- Declaring Type: {resolver.ToWikiLink(declaringType.Id, declaringType.Name)}");
        }
        else if (endpoint.DeclaringTypeId is { } unresolvedDeclaringTypeId)
        {
            sb.AppendLine($"- Declaring Type: `{unresolvedDeclaringTypeId.Value}`");
        }
        else
        {
            sb.AppendLine("- Declaring Type: none");
        }

        if (endpoint.DeclaringMethodId is { } declaringMethodId && methodById.TryGetValue(declaringMethodId, out var declaringMethod))
        {
            sb.AppendLine($"- Declaring Method: {resolver.ToWikiLink(declaringMethod.Id, FormatMethodLinkAlias(declaringMethod))}");
        }
        else if (endpoint.DeclaringMethodId is { } unresolvedDeclaringMethodId)
        {
            sb.AppendLine($"- Declaring Method: `{unresolvedDeclaringMethodId.Value}`");
        }
        else
        {
            sb.AppendLine("- Declaring Method: none");
        }

        if (endpoint.GroupId is { } groupId && endpointGroupById.TryGetValue(groupId, out var endpointGroup))
        {
            sb.AppendLine($"- Endpoint Group: {resolver.ToWikiLink(endpointGroup.Id, endpointGroup.Name)}");
        }
        else
        {
            sb.AppendLine("- Endpoint Group: none");
        }

        sb.AppendLine();
        sb.AppendLine("## Declaration Files");
        if (endpoint.DeclarationFileIds.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var declarationFileId in endpoint.DeclarationFileIds)
            {
                if (fileById.TryGetValue(declarationFileId, out var file))
                {
                    sb.AppendLine($"- {resolver.ToWikiLink(file.Id, file.Path)}");
                }
            }
        }

        var frontMatter = new List<KeyValuePair<string, string>>
        {
            KeyValue("entity_id", endpoint.Id.Value),
            KeyValue("entity_type", "endpoint"),
            KeyValue("repository_id", repositoryId),
            KeyValue("endpoint_family", endpoint.Family),
            KeyValue("endpoint_kind", endpoint.Kind),
            KeyValue("endpoint_http_method", endpoint.HttpMethod),
            KeyValue("endpoint_route_key", endpoint.NormalizedRouteKey),
        };

        return new WikiPage(
            RelativePath: resolver.GetPath(endpoint.Id),
            Title: endpoint.Name,
            Markdown: WithFrontMatter(frontMatter, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderFilePage(
        RepositoryNode repository,
        FileNode file,
        IReadOnlyDictionary<EntityId, IReadOnlyList<NamespaceDeclarationNode>> namespaceBacklinksByFileId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<TypeDeclarationNode>> typeBacklinksByFileId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<MemberDeclarationNode>> memberBacklinksByFileId,
        IReadOnlyDictionary<EntityId, IReadOnlyList<MethodDeclarationNode>> methodBacklinksByFileId,
        WikiPathResolver resolver,
        int? maxMergeEntriesPerFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# File: {file.Path}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{file.Path}`");
        sb.AppendLine($"- classification: {file.Classification}");
        sb.AppendLine($"- is_solution_member: {file.IsSolutionMember.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- branch_snapshot: {repository.HeadBranch}");
        sb.AppendLine($"- mainline_branch: {repository.MainlineBranch}");
        sb.AppendLine();
        sb.AppendLine("## History Summary");
        sb.AppendLine($"- edit_count: {file.EditCount}");

        if (file.LastChange is not null)
        {
            sb.AppendLine($"- last_change_commit: `{file.LastChange.CommitSha}`");
            sb.AppendLine($"- last_changed_at_utc: `{file.LastChange.TimestampUtc}`");
            sb.AppendLine($"- last_changed_by: {file.LastChange.AuthorName} <{file.LastChange.AuthorEmail}>");
        }

        sb.AppendLine();
        sb.AppendLine("## Merge To Mainline");

        IEnumerable<FileMergeEventNode> mergeEvents = file.MergeToMainlineEvents
            .OrderByDescending(x => ParseTimestamp(x.TimestampUtc))
            .ThenBy(x => x.MergeCommitSha, StringComparer.Ordinal);

        if (maxMergeEntriesPerFile is int cap)
        {
            mergeEvents = mergeEvents.Take(Math.Max(cap, 0));
        }

        foreach (var merge in mergeEvents)
        {
            sb.AppendLine($"- merge_commit: `{merge.MergeCommitSha}`");
            sb.AppendLine($"  merged_at_utc: `{merge.TimestampUtc}`");
            sb.AppendLine($"  author: {merge.AuthorName} <{merge.AuthorEmail}>");
            sb.AppendLine($"  target_branch: {merge.TargetBranch}");
            sb.AppendLine($"  source_branch_file_commit_count: {merge.SourceBranchFileCommitCount}");
        }

        sb.AppendLine();
        sb.AppendLine("## Declared Symbols");

        sb.AppendLine("### Namespaces");
        if (namespaceBacklinksByFileId.TryGetValue(file.Id, out var namespaces))
        {
            foreach (var namespaceDeclaration in namespaces)
            {
                sb.AppendLine($"- {resolver.ToWikiLink(namespaceDeclaration.Id, namespaceDeclaration.Name)}");
            }
        }
        else
        {
            sb.AppendLine("- none");
        }

        sb.AppendLine("### Types");
        if (typeBacklinksByFileId.TryGetValue(file.Id, out var types))
        {
            foreach (var typeDeclaration in types)
            {
                sb.AppendLine($"- {resolver.ToWikiLink(typeDeclaration.Id, typeDeclaration.Name)}");
            }
        }
        else
        {
            sb.AppendLine("- none");
        }

        sb.AppendLine("### Members");
        if (memberBacklinksByFileId.TryGetValue(file.Id, out var members))
        {
            foreach (var member in members)
            {
                sb.AppendLine($"- {member.Name} ({member.Kind.ToString().ToLowerInvariant()})");
            }
        }
        else
        {
            sb.AppendLine("- none");
        }

        sb.AppendLine("### Methods");
        if (methodBacklinksByFileId.TryGetValue(file.Id, out var methods))
        {
            foreach (var method in methods)
            {
                sb.AppendLine($"- {resolver.ToWikiLink(method.Id, FormatMethodLinkAlias(method))} ({method.Kind.ToString().ToLowerInvariant()})");
            }
        }
        else
        {
            sb.AppendLine("- none");
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(file.Id),
            Title: file.Path,
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", file.Id.Value),
                    KeyValue("entity_type", "file"),
                    KeyValue("repository_id", repository.Id.Value),
                    KeyValue("file_name", file.Name),
                    KeyValue("file_path", file.Path),
                ],
                sb.ToString().TrimEnd()));
    }

    private static bool TryResolvePrimaryProject(
        TypeDeclarationNode typeDeclaration,
        IReadOnlyDictionary<EntityId, FileNode> fileById,
        IReadOnlyList<ProjectNode> projects,
        out ProjectNode primaryProject)
    {
        var orderedDeclarationFilePaths = typeDeclaration.DeclarationLocations
            .OrderBy(x => x.FilePath, StringComparer.Ordinal)
            .ThenBy(x => x.Line)
            .ThenBy(x => x.Column)
            .Select(x => x.FilePath)
            .Concat(typeDeclaration.DeclarationFileIds
                .Where(fileById.ContainsKey)
                .Select(id => fileById[id].Path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var declarationFilePath in orderedDeclarationFilePaths)
        {
            var project = projects
                .Where(candidate => IsFileWithinProject(candidate.Path, declarationFilePath))
                .OrderByDescending(candidate => candidate.Path.Length)
                .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
                .FirstOrDefault();

            if (project is not null)
            {
                primaryProject = project;
                return true;
            }
        }

        primaryProject = null!;
        return false;
    }

    private static bool IsFileWithinProject(string projectPath, string filePath)
    {
        var projectDirectory = projectPath.Replace('\\', '/');
        var lastSlash = projectDirectory.LastIndexOf('/');
        var directoryPath = lastSlash >= 0 ? projectDirectory[..lastSlash] : string.Empty;
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        return filePath.StartsWith(directoryPath + "/", StringComparison.Ordinal)
               || filePath.Equals(directoryPath, StringComparison.Ordinal);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static IReadOnlyList<WikiPage> RenderHotspotPages(ProjectStructureWikiModel model, WikiPathResolver resolver)
    {
        return GetHotspotPageDescriptors()
            .Select(descriptor => RenderHotspotPage(model, resolver, descriptor.Kind, descriptor.Title, descriptor.RelativePath, descriptor.KindSlug))
            .ToArray();
    }

    private static IReadOnlyList<IndexRow> BuildHotspotIndexRows(string repositoryId, WikiPathResolver resolver)
    {
        return GetHotspotPageDescriptors()
            .Select(descriptor => new IndexRow(
                Name: descriptor.Title,
                Path: descriptor.RelativePath,
                EntityId: $"hotspot:{descriptor.KindSlug}:{repositoryId}",
                PageLink: resolver.ToMarkdownLink(descriptor.RelativePath, descriptor.Title)))
            .ToArray();
    }

    private static WikiPage RenderHotspotPage(
        ProjectStructureWikiModel model,
        WikiPathResolver resolver,
        HotspotTargetKind targetKind,
        string title,
        string relativePath,
        string kindSlug)
    {
        var primaryRankings = model.Hotspots.PrimaryRankings
            .Where(x => x.TargetKind == targetKind)
            .OrderBy(x => x.MetricKind)
            .ToArray();
        var compositeRanking = model.Hotspots.CompositeRankings
            .FirstOrDefault(x => x.TargetKind == targetKind);
        var effectiveConfig = model.Hotspots.EffectiveConfig;

        var sb = new StringBuilder();
        sb.AppendLine($"# Hotspots: {title}");
        sb.AppendLine();
        sb.AppendLine("## Effective Config");
        sb.AppendLine($"- Top N: `{effectiveConfig.EffectiveTopN}`");
        sb.AppendLine($"- Unbounded: `{effectiveConfig.Unbounded.ToString().ToLowerInvariant()}`");
        sb.AppendLine();
        sb.AppendLine("| metric | composite_weight | low | medium | high | critical |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var metric in primaryRankings.Select(x => x.MetricKind).Distinct().OrderBy(x => x))
        {
            effectiveConfig.CompositeWeights.TryGetValue(metric, out var weight);
            effectiveConfig.Thresholds.TryGetValue(metric, out var thresholds);
            thresholds ??= new HotspotSeverityThresholds(0d, 0d, 0d, 0d);

            sb.AppendLine($"| {EscapePipes(FormatHotspotMetricKind(metric))} | `{weight.ToString("0.####", CultureInfo.InvariantCulture)}` | `{thresholds.Low.ToString("0.####", CultureInfo.InvariantCulture)}` | `{thresholds.Medium.ToString("0.####", CultureInfo.InvariantCulture)}` | `{thresholds.High.ToString("0.####", CultureInfo.InvariantCulture)}` | `{thresholds.Critical.ToString("0.####", CultureInfo.InvariantCulture)}` |");
        }

        sb.AppendLine();
        sb.AppendLine("## Primary Rankings");

        if (primaryRankings.Length == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var ranking in primaryRankings)
            {
                sb.AppendLine($"### {FormatHotspotMetricKind(ranking.MetricKind)}");
                sb.AppendLine("| rank | entity | raw_value | normalized_score | severity |");
                sb.AppendLine("| --- | --- | --- | --- | --- |");

                foreach (var row in ranking.Rows)
                {
                    var entityLink = TryCreateHotspotEntityLink(resolver, row.EntityId, row.DisplayName);
                    sb.AppendLine($"| `{row.Rank}` | {EscapePipes(entityLink)} | `{row.RawValue.ToString("0.####", CultureInfo.InvariantCulture)}` | `{row.NormalizedScore.ToString("0.####", CultureInfo.InvariantCulture)}` | `{row.Severity.ToString().ToLowerInvariant()}` |");
                }

                if (ranking.Rows.Count == 0)
                {
                    sb.AppendLine("| `-` | none | `-` | `-` | `none` |");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("## Composite Ranking");
        sb.AppendLine("| rank | entity | composite_score | severity |");
        sb.AppendLine("| --- | --- | --- | --- |");

        if (compositeRanking is null || compositeRanking.Rows.Count == 0)
        {
            sb.AppendLine("| `-` | none | `-` | `none` |");
        }
        else
        {
            foreach (var row in compositeRanking.Rows)
            {
                var entityLink = TryCreateHotspotEntityLink(resolver, row.EntityId, row.DisplayName);
                sb.AppendLine($"| `{row.Rank}` | {EscapePipes(entityLink)} | `{row.CompositeScore.ToString("0.####", CultureInfo.InvariantCulture)}` | `{row.Severity.ToString().ToLowerInvariant()}` |");
            }
        }

        return new WikiPage(
            RelativePath: relativePath,
            Title: $"Hotspots {title}",
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", $"hotspot:{kindSlug}:{model.Repository.Id.Value}"),
                    KeyValue("entity_type", "hotspot"),
                    KeyValue("repository_id", model.Repository.Id.Value),
                    KeyValue("hotspot_kind", kindSlug),
                ],
                sb.ToString().TrimEnd()));
    }

    private static string TryCreateHotspotEntityLink(WikiPathResolver resolver, EntityId entityId, string displayName)
    {
        try
        {
            return resolver.ToMarkdownLink(entityId, displayName);
        }
        catch (InvalidOperationException)
        {
            return displayName;
        }
    }

    private static string FormatHotspotMetricKind(HotspotMetricKind metricKind)
    {
        return metricKind switch
        {
            HotspotMetricKind.MethodCyclomaticComplexity => "method_cyclomatic_complexity",
            HotspotMetricKind.MethodCognitiveComplexity => "method_cognitive_complexity",
            HotspotMetricKind.MethodHalsteadVolume => "method_halstead_volume",
            HotspotMetricKind.MethodMaintainabilityIndex => "method_maintainability_index",
            HotspotMetricKind.TypeCboDeclaration => "type_cbo_declaration",
            HotspotMetricKind.TypeCboMethodBody => "type_cbo_method_body",
            HotspotMetricKind.TypeCboTotal => "type_cbo_total",
            HotspotMetricKind.ScopeAverageCyclomaticComplexity => "scope_average_cyclomatic_complexity",
            HotspotMetricKind.ScopeAverageCognitiveComplexity => "scope_average_cognitive_complexity",
            HotspotMetricKind.ScopeAverageMaintainabilityIndex => "scope_average_maintainability_index",
            HotspotMetricKind.ScopeAverageCboTotal => "scope_average_cbo_total",
            HotspotMetricKind.Composite => "composite",
            _ => "unknown",
        };
    }

    private static IReadOnlyList<(HotspotTargetKind Kind, string Title, string RelativePath, string KindSlug)> GetHotspotPageDescriptors()
    {
        return
        [
            (HotspotTargetKind.Method, "Methods", "hotspots/methods.md", "methods"),
            (HotspotTargetKind.Type, "Types", "hotspots/types.md", "types"),
            (HotspotTargetKind.File, "Files", "hotspots/files.md", "files"),
            (HotspotTargetKind.Namespace, "Namespaces", "hotspots/namespaces.md", "namespaces"),
            (HotspotTargetKind.Project, "Projects", "hotspots/projects.md", "projects"),
            (HotspotTargetKind.Repository, "Repository", "hotspots/repository.md", "repository"),
        ];
    }

    private static WikiPage RenderIndexPage(
        ProjectStructureWikiModel model,
        WikiPathResolver resolver,
        string indexPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Repository Index");
        sb.AppendLine();
        sb.AppendLine("## Guidance");
        sb.AppendLine($"- {resolver.ToMarkdownLink("guidance/human.md", "Human Guide")}");
        sb.AppendLine($"- {resolver.ToMarkdownLink("guidance/llm-contract.md", "LLM Contract")}");
        sb.AppendLine();

        AppendTable(
            sb,
            "Repositories",
            [new IndexRow(model.Repository.Name, model.Repository.Path, model.Repository.Id.Value, resolver.ToMarkdownLink(model.Repository.Id, model.Repository.Name))]);

        AppendTable(
            sb,
            "Solutions",
            model.Solutions
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToMarkdownLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Projects",
            model.Projects
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToMarkdownLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Packages",
            model.Packages
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Name, x.Id.Value, resolver.ToMarkdownLink(x.Id, x.Name)))
                .Concat(
                    model.DependencyAttribution.DeclarationUnknown.UsageCount > 0
                    || model.DependencyAttribution.MethodBodyUnknown.UsageCount > 0
                        ? [new IndexRow(
                            "Unknown Package Attribution",
                            "packages/unknown-package-attribution.md",
                            $"dependency-attribution:unknown:{model.Repository.Id.Value}",
                            resolver.ToMarkdownLink("packages/unknown-package-attribution.md", "Unknown Package Attribution"))]
                        : [])
                .ToArray());

        AppendTable(
            sb,
            "Namespaces",
            model.Declarations.Namespaces
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToMarkdownLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Types",
            model.Declarations.Types
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToMarkdownLink(x.Id, x.Name)))
                .ToArray());

        var typeById = model.Declarations.Types.ToDictionary(x => x.Id, x => x);
        AppendTable(
            sb,
            "Methods",
            model.Declarations.Methods.Declarations
                .OrderBy(x => x.Signature, StringComparer.Ordinal)
                .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
                .Select(x =>
                {
                    var declaringTypePath = typeById.TryGetValue(x.DeclaringTypeId, out var declaringType)
                        ? declaringType.Path
                        : "<unknown-type>";
                    return new IndexRow(FormatMethodLinkAlias(x), $"{declaringTypePath}::{x.Signature}", x.Id.Value, resolver.ToMarkdownLink(x.Id, FormatMethodLinkAlias(x)));
                })
                .ToArray());

        AppendTable(
            sb,
            "Endpoint Groups",
            model.Endpoints.Groups
                .OrderBy(x => x.Family, StringComparer.Ordinal)
                .ThenBy(x => x.NormalizedRoutePrefix, StringComparer.Ordinal)
                .ThenBy(x => x.CanonicalKey, StringComparer.Ordinal)
                .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
                .Select(x => new IndexRow(
                    $"{x.Family}:{x.Name}",
                    string.IsNullOrWhiteSpace(x.NormalizedRoutePrefix) ? x.CanonicalKey : x.NormalizedRoutePrefix,
                    x.Id.Value,
                    resolver.ToMarkdownLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Endpoints",
            model.Endpoints.Endpoints
                .OrderBy(x => x.Family, StringComparer.Ordinal)
                .ThenBy(x => x.NormalizedRouteKey, StringComparer.Ordinal)
                .ThenBy(x => x.HttpMethod, StringComparer.Ordinal)
                .ThenBy(x => x.CanonicalSignature, StringComparer.Ordinal)
                .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
                .Select(x => new IndexRow(
                    $"{x.Family}:{x.HttpMethod} {x.NormalizedRouteKey}",
                    x.CanonicalSignature,
                    x.Id.Value,
                    resolver.ToMarkdownLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Hotspots",
            BuildHotspotIndexRows(model.Repository.Id.Value, resolver));

        AppendTable(
            sb,
            "Files",
            model.Files
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToMarkdownLink(x.Id, x.Path)))
                .ToArray());

        return new WikiPage(
            RelativePath: indexPath,
            Title: "Repository Index",
            Markdown: WithFrontMatter(
                [
                    KeyValue("entity_id", $"index:{model.Repository.Id.Value}"),
                    KeyValue("entity_type", "index"),
                    KeyValue("repository_id", model.Repository.Id.Value),
                ],
                sb.ToString().TrimEnd()));
    }

    private static void AppendTable(StringBuilder sb, string title, IReadOnlyList<IndexRow> rows)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine("| name | path | entity_id | page_link |");
        sb.AppendLine("| --- | --- | --- | --- |");

        foreach (var row in rows)
        {
            sb.AppendLine($"| {EscapePipes(row.Name)} | {EscapePipes(row.Path)} | `{row.EntityId}` | {row.PageLink} |");
        }

        sb.AppendLine();
    }

    private static string EscapePipes(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string ToWikiLink(string target, string alias)
    {
        return $"[[{target}|{alias}]]";
    }

    private static string BuildBacklogItemMarkdownLink(string backlogId, string backlogAnchor, string headBranch)
    {
        var branch = string.IsNullOrWhiteSpace(headBranch) ? "develop" : headBranch.Trim();
        return $"[{backlogId}](https://github.com/vbfg1973/code-llm-wiki/blob/{branch}/plans/BACKLOG.md#{backlogAnchor})";
    }

    private static KeyValuePair<string, string> KeyValue(string key, string value) => new(key, value);

    private static string ToInlineList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        return $"[{string.Join(", ", values)}]";
    }

    private static string WithFrontMatter(IReadOnlyList<KeyValuePair<string, string>> values, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        foreach (var pair in values)
        {
            sb.AppendLine($"{pair.Key}: {pair.Value}");
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }

    private sealed record InheritedPackageTypeUsageRow(
        EntityId PackageId,
        string ExternalTypeDisplayName,
        EntityId InternalTypeId,
        string InternalTypeName,
        int UsageCount);

    private sealed record InheritedPackageTypeUsageNode(
        string ExternalTypeDisplayName,
        int UsageCount,
        IReadOnlyList<InheritedPackageInternalTypeUsageNode> InternalTypes);

    private sealed record InheritedPackageInternalTypeUsageNode(
        EntityId InternalTypeId,
        string InternalTypeName,
        int UsageCount);

    private sealed record IndexRow(string Name, string Path, string EntityId, string PageLink);
}
