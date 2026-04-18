namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record ProjectStructureWikiModel(
    RepositoryNode Repository,
    IReadOnlyList<SolutionNode> Solutions,
    IReadOnlyList<ProjectNode> Projects);
