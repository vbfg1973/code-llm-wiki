using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record SolutionNode(EntityId Id, string Name, string Path, IReadOnlyList<EntityId> ProjectIds);
