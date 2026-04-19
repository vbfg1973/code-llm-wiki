using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record RepositoryNode(
    EntityId Id,
    string Name,
    string Path,
    string HeadBranch,
    string MainlineBranch);
