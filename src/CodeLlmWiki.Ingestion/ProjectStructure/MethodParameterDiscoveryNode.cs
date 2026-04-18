namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record MethodParameterDiscoveryNode(
    string Name,
    int Ordinal,
    string? DeclaredTypeName);
