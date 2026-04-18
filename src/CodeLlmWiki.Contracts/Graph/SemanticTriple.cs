namespace CodeLlmWiki.Contracts.Graph;

public sealed record SemanticTriple(GraphNode Subject, PredicateId Predicate, GraphNode Object);
