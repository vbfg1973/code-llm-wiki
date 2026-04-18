namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record NamespaceDiscoveryResult(
    IReadOnlyList<NamespaceDiscoveryNode> Namespaces,
    IReadOnlyList<TypeDiscoveryNode> Types);
