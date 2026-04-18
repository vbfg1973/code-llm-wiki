using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Contracts.Graph;

public sealed record EntityNode(EntityId Id) : GraphNode;
