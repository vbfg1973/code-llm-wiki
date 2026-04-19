namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record ProjectStructureWikiModel(
    RepositoryNode Repository,
    IReadOnlyList<SolutionNode> Solutions,
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<PackageNode> Packages,
    IReadOnlyList<FileNode> Files,
    IReadOnlyList<SubmoduleNode> Submodules)
{
    public DeclarationCatalog Declarations { get; init; } = DeclarationCatalog.Empty;
    public EndpointCatalog Endpoints { get; init; } = EndpointCatalog.Empty;
    public DependencyAttributionCatalog DependencyAttribution { get; init; } = DependencyAttributionCatalog.Empty;
    public StructuralMetricRollupCatalog StructuralMetrics { get; init; } = StructuralMetricRollupCatalog.Empty;
    public HotspotRankingCatalog Hotspots { get; init; } = HotspotRankingCatalog.Empty;
}
