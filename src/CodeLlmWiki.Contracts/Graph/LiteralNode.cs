namespace CodeLlmWiki.Contracts.Graph;

public sealed record LiteralNode(object? Value, string? Datatype = null, string? Language = null) : GraphNode;
