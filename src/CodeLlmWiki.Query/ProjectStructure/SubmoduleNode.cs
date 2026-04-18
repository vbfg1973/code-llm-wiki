using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record SubmoduleNode(
    EntityId Id,
    string Name,
    string Path,
    string Url);
