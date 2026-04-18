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
}
