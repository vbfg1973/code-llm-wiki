using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record FileNode(
    EntityId Id,
    string Name,
    string Path,
    string Classification,
    bool IsSolutionMember);
